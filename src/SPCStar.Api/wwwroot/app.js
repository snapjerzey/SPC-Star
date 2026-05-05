const state = {
  user: null,
  snapshot: null,
  contexts: [],
  selectedPlans: [],
  trendCharacteristic: "",
  activeLock: null,
  users: [],
  roles: []
};

const $ = (id) => document.getElementById(id);

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `${response.status} ${response.statusText}`);
  }
  return response.json();
}

function setStatus(element, text, kind = "neutral") {
  element.textContent = text;
  element.className = `status-pill ${kind}`;
}

function inspectionSets() {
  const map = new Map();
  state.snapshot.inspectionPlans.forEach((plan) => {
    const key = `${plan.partNum}|${plan.processCode}|${plan.operationSeq}`;
    if (!map.has(key)) {
      map.set(key, {
        key,
        partNum: plan.partNum,
        partDescription: plan.partDescription,
        processCode: plan.processCode,
        processDescription: plan.processDescription,
        operationSeq: plan.operationSeq,
        plans: []
      });
    }
    map.get(key).plans.push(plan);
  });
  return [...map.values()];
}

function selectedInspectionSet() {
  const partNum = $("partNum").value.trim();
  return inspectionSets().find((set) => set.partNum.toLowerCase() === partNum.toLowerCase()) || null;
}

function selectedValues() {
  const set = selectedInspectionSet();
  return {
    jobNum: $("jobNum").value.trim(),
    resourceId: $("resourceId").value,
    set
  };
}

async function login(event) {
  event.preventDefault();
  const userName = $("userName").value.trim();
  const password = $("password").value;
  state.user = await api("/auth/login", {
    method: "POST",
    body: JSON.stringify({ userName, password })
  });
  setStatus($("userBadge"), `${state.user.userName} (${state.user.roles.join(", ")})`, "ok");
  $("logoutButton").classList.remove("hidden");
  $("loginPanel").classList.add("hidden");
  $("workPanel").classList.remove("hidden");
  if (canManageSetup()) {
    $("navTabs").classList.remove("hidden");
    await loadSetupAdmin();
  }
  await loadSnapshot();
}

async function loadSnapshot() {
  state.snapshot = await api("/sync/setup-snapshot");
  $("setupVersion").textContent = `Setup ${state.snapshot.setupVersion}`;
  fillDatalist($("jobOptions"), state.snapshot.jobs, (job) => job.jobNum);
  $("jobNum").value = state.snapshot.jobs[0]?.jobNum || "";
  fillSelect($("resourceId"), state.snapshot.resources, (resource) => resource.resourceId, (resource) => resource.resourceId);
  fillDatalist($("partOptions"), state.snapshot.parts, (part) => part.partNum);
  $("partNum").value = state.snapshot.parts[0]?.partNum || "";
  updatePartFromJob();
  if (canManageSetup()) {
    renderPartReviewControls();
    renderPartReview();
    $("summaryJobNum").value = $("summaryJobNum").value || state.snapshot.jobs[0]?.jobNum || "";
  }
  if (selectedInspectionSet() && $("jobNum").value && state.snapshot.resources.length) {
    await loadContext();
  }
}

function fillSelect(select, rows, valueOf, labelOf) {
  select.innerHTML = "";
  rows.forEach((row, index) => {
    const option = document.createElement("option");
    option.value = valueOf(row, index);
    option.textContent = labelOf(row, index);
    select.appendChild(option);
  });
}

function fillDatalist(list, rows, valueOf) {
  list.innerHTML = "";
  rows.forEach((row) => {
    const option = document.createElement("option");
    option.value = valueOf(row);
    list.appendChild(option);
  });
}

function updatePartFromJob() {
  const job = state.snapshot.jobs.find((item) => item.jobNum.toLowerCase() === $("jobNum").value.trim().toLowerCase());
  if (job && state.snapshot.parts.some((part) => part.partNum.toLowerCase() === job.partNum.toLowerCase())) {
    $("partNum").value = job.partNum;
  }
}

async function loadContext(event) {
  event?.preventDefault();
  updatePartFromJob();
  const { jobNum, resourceId, set } = selectedValues();
  if (!set || !jobNum || !resourceId) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext();
    return;
  }
  state.selectedPlans = set.plans;
  state.contexts = await Promise.all(set.plans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  renderContext();
}

function renderEmptyContext() {
  $("contextTitle").textContent = "No inspection loaded";
  $("contextSubtitle").textContent = "Enter a job number, machine, and part number, then start inspecting.";
  renderLock(null);
  $("variableList").innerHTML = "";
  $("meanSummary").innerHTML = "";
  $("trendCharacteristic").innerHTML = "";
  drawTrend([]);
}

async function loadVariableContext(jobNum, resourceId, plan) {
  const params = new URLSearchParams({
    jobNum,
    partNum: plan.partNum,
    processCode: plan.processCode,
    operationSeq: String(plan.operationSeq),
    resourceId,
    characteristicName: plan.characteristicName
  });
  return api(`/work-context?${params}`);
}

function renderContext() {
  const { jobNum, resourceId, set } = selectedValues();
  $("contextTitle").textContent = `${jobNum} ${resourceId}`;
  $("contextSubtitle").textContent = `${set.partNum} / ${set.processCode} ${set.operationSeq}`;
  state.activeLock = state.contexts.find((context) => context.activeLock)?.activeLock || null;
  renderLock(state.activeLock);
  renderVariables();
  renderMeanSummary();
  renderTrendChoices();
  loadTrend();
}

function renderLock(activeLock) {
  const banner = $("lockBanner");
  const panel = $("overridePanel");
  if (!activeLock) {
    banner.classList.add("hidden");
    banner.textContent = "";
    panel.classList.add("hidden");
    $("overrideMessage").textContent = "";
    return;
  }
  banner.classList.remove("hidden");
  banner.textContent = `LOCKED: ${ruleLabel(activeLock.ruleTriggered)} at ${formatTime(activeLock.lockedAt)}`;
  panel.classList.remove("hidden");
  $("overrideUserName").value = canCurrentUserOverride() ? state.user.userName : "";
  $("godReasonLabel").classList.toggle("hidden", !state.user?.roles?.includes("GOD"));
}

function renderVariables() {
  const list = $("variableList");
  list.innerHTML = "";
  state.selectedPlans.forEach((plan, index) => {
    const context = state.contexts[index];
    const card = document.createElement("section");
    card.className = "variable-card";
    card.innerHTML = `
      <div>
        <div class="variable-title">
          <strong>${plan.characteristicName}</strong>
          <span>${plan.unitOfMeasure}</span>
        </div>
        <div class="sample-meta">
          <span>Sample size ${plan.sampleSize}</span>
          <span>${formatFrequency(plan)}</span>
        </div>
        <div class="limit-grid">
          <div><span>LSL</span><strong>${formatNumber(context.lowerSpecLimit)}</strong></div>
          <div><span>Target</span><strong>${formatNumber(plan.nominal)}</strong></div>
          <div><span>USL</span><strong>${formatNumber(context.upperSpecLimit)}</strong></div>
          <div><span>LCL</span><strong>${formatNumber(context.lowerControlLimit)}</strong></div>
          <div><span>Center</span><strong>${formatNumber(plan.nominal)}</strong></div>
          <div><span>UCL</span><strong>${formatNumber(context.upperControlLimit)}</strong></div>
        </div>
      </div>
      <div class="sample-inputs">
        ${Array.from({ length: plan.sampleSize }, (_, sampleIndex) => `
          <label>
            Sample ${sampleIndex + 1}
            <input class="measurement-input" data-plan-index="${index}" data-sample-index="${sampleIndex}" type="number" step="0.0001" inputmode="decimal" placeholder="0.0000">
          </label>`).join("")}
      </div>`;
    list.appendChild(card);
  });
}

function renderMeanSummary() {
  const summary = $("meanSummary");
  summary.innerHTML = "";
  state.selectedPlans.forEach((plan, index) => {
    const points = state.contexts[index]?.recentMeasurements || [];
    const mean = points.length
      ? points.reduce((total, point) => total + Number(point.value), 0) / points.length
      : null;
    const item = document.createElement("div");
    item.className = "mean-item";
    item.innerHTML = `
      <span>${plan.characteristicName}</span>
      <strong>${formatNumber(mean)}</strong>
      <small>${points.length} pt${points.length === 1 ? "" : "s"}</small>`;
    summary.appendChild(item);
  });
}

function formatFrequency(plan) {
  const unit = {
    Minutes: "minutes",
    Hours: "hours",
    Pieces: "parts",
    StartOfJob: "start of job",
    MaterialChange: "material change",
    ToolChange: "tool change",
    Restart: "restart"
  }[plan.frequencyUnit] || plan.frequencyUnit;

  if (plan.frequencyType === "Quantity") {
    return `Every ${plan.frequencyValue} ${unit}`;
  }

  if (plan.frequencyType === "Time") {
    return `Every ${plan.frequencyValue} ${unit}`;
  }

  return `At ${unit}`;
}

async function submitMeasurement(event) {
  event.preventDefault();
  const { jobNum, resourceId } = selectedValues();
  const inputs = [...document.querySelectorAll(".measurement-input")];
  const entries = inputs
    .map((input) => ({ input, plan: state.selectedPlans[Number(input.dataset.planIndex)], value: Number(input.value) }))
    .filter((entry) => inputHasValue(entry.input));

  if (!entries.length) {
    showEntryMessage("Enter at least one measurement value.", "error");
    return;
  }

  if (entries.some((entry) => !Number.isFinite(entry.value))) {
    showEntryMessage("Every entered measurement must be numeric.", "error");
    return;
  }

  try {
    for (const entry of entries) {
      await api("/inspections/measurements", {
        method: "POST",
        body: JSON.stringify({
          jobNum,
          partNum: entry.plan.partNum,
          processCode: entry.plan.processCode,
          operationSeq: entry.plan.operationSeq,
          resourceId,
          characteristicName: entry.plan.characteristicName,
          value: entry.value,
          timestamp: new Date().toISOString(),
          operatorUserId: state.user.userName,
          deviceId: "browser-dev",
          clientRecordId: newClientRecordId(),
          submittedAt: new Date().toISOString()
        })
      });
    }
    inputs.forEach((input) => { input.value = ""; });
    showEntryMessage(`${entries.length} measurement${entries.length === 1 ? "" : "s"} submitted.`, "ok");
    await loadContext();
  } catch (error) {
    showEntryMessage("Measurement rejected. " + readableError(error), "error");
  }
}

function inputHasValue(input) {
  return input.value.trim().length > 0;
}

function showEntryMessage(message, kind) {
  $("entryMessage").textContent = message;
  $("entryMessage").className = `message ${kind}`;
}

function renderTrendChoices() {
  const select = $("trendCharacteristic");
  const previous = state.trendCharacteristic || select.value;
  fillSelect(select, state.selectedPlans, (plan) => plan.characteristicName, (plan) => plan.characteristicName);
  if (state.selectedPlans.some((plan) => plan.characteristicName === previous)) {
    select.value = previous;
  }
  state.trendCharacteristic = select.value;
}

async function loadTrend() {
  const { jobNum, resourceId, set } = selectedValues();
  if (!set || !state.trendCharacteristic) {
    drawTrend([]);
    return;
  }

  const data = await api("/charts/data", {
    method: "POST",
    body: JSON.stringify({
      chartType: "IndividualsMovingRange",
      jobNum,
      partNum: set.partNum,
      resourceId,
      characteristicName: state.trendCharacteristic,
      from: null,
      to: null
    })
  });

  drawTrend(data.points, data);
}

function drawTrend(points, data = {}) {
  const canvas = $("trendCanvas");
  const ctx = canvas.getContext("2d");
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, width, height);

  const padding = { left: 42, right: 18, top: 18, bottom: 34 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const values = points.map((point) => Number(point.value));
  const limitValues = [data.lowerControlLimit, data.upperControlLimit, data.lowerSpecLimit, data.upperSpecLimit]
    .filter((value) => value !== null && value !== undefined)
    .map(Number);

  if (!values.length) {
    $("trendMessage").textContent = "No trend data yet.";
    drawChartFrame(ctx, padding, plotWidth, plotHeight);
    return;
  }

  $("trendMessage").textContent = `${points.length} point${points.length === 1 ? "" : "s"} for ${state.trendCharacteristic}`;
  const min = Math.min(...values, ...limitValues);
  const max = Math.max(...values, ...limitValues);
  const spread = max === min ? 1 : max - min;
  const low = min - spread * 0.1;
  const high = max + spread * 0.1;
  const x = (index) => padding.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
  const y = (value) => padding.top + (1 - ((Number(value) - low) / (high - low))) * plotHeight;

  drawChartFrame(ctx, padding, plotWidth, plotHeight);
  drawLimitLine(ctx, y, data.upperControlLimit, "UCL", "#b54708", width, padding);
  drawLimitLine(ctx, y, data.lowerControlLimit, "LCL", "#b54708", width, padding);
  drawLimitLine(ctx, y, data.upperSpecLimit, "USL", "#b42318", width, padding);
  drawLimitLine(ctx, y, data.lowerSpecLimit, "LSL", "#b42318", width, padding);

  ctx.strokeStyle = "#0f766e";
  ctx.lineWidth = 2;
  ctx.beginPath();
  points.forEach((point, index) => {
    if (index === 0) ctx.moveTo(x(index), y(point.value));
    else ctx.lineTo(x(index), y(point.value));
  });
  ctx.stroke();

  points.forEach((point, index) => {
    ctx.beginPath();
    ctx.fillStyle = point.hasRuleViolation ? "#b42318" : "#0f766e";
    ctx.arc(x(index), y(point.value), 4, 0, Math.PI * 2);
    ctx.fill();
  });

  ctx.fillStyle = "#667085";
  ctx.font = "12px Segoe UI, Arial";
  ctx.fillText(formatNumber(low), 6, padding.top + plotHeight);
  ctx.fillText(formatNumber(high), 6, padding.top + 8);
}

function drawChartFrame(ctx, padding, plotWidth, plotHeight) {
  ctx.strokeStyle = "#d9dee7";
  ctx.lineWidth = 1;
  ctx.strokeRect(padding.left, padding.top, plotWidth, plotHeight);
  ctx.strokeStyle = "#eef2f6";
  for (let index = 1; index < 4; index++) {
    const y = padding.top + (plotHeight / 4) * index;
    ctx.beginPath();
    ctx.moveTo(padding.left, y);
    ctx.lineTo(padding.left + plotWidth, y);
    ctx.stroke();
  }
}

function drawLimitLine(ctx, scaleY, value, label, color, width, padding) {
  if (value === null || value === undefined) return;
  const y = scaleY(value);
  ctx.strokeStyle = color;
  ctx.lineWidth = 1;
  ctx.setLineDash([5, 5]);
  ctx.beginPath();
  ctx.moveTo(padding.left, y);
  ctx.lineTo(width - padding.right, y);
  ctx.stroke();
  ctx.setLineDash([]);
  ctx.fillStyle = color;
  ctx.font = "11px Segoe UI, Arial";
  ctx.fillText(label, width - padding.right - 28, y - 4);
}

async function saveMaterialChange(event) {
  event.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  try {
    await api("/material-changes", {
      method: "POST",
      body: JSON.stringify({
        jobNum,
        partNum: set.partNum,
        materialPartNum: $("materialPartNum").value.trim(),
        oldLotNum: "",
        newLotNum: $("newLotNum").value.trim(),
        quantityLoaded: optionalNumber("quantityLoaded"),
        resourceId,
        operatorUserId: state.user.userName,
        timestamp: new Date().toISOString(),
        reason: $("materialReason").value,
        deviceId: "browser-dev",
        clientRecordId: newClientRecordId(),
        submittedAt: new Date().toISOString()
      })
    });
    $("newLotNum").value = "";
    $("quantityLoaded").value = "";
    $("materialMessage").textContent = "Material event saved.";
    $("materialMessage").className = "message ok";
  } catch (error) {
    $("materialMessage").textContent = readableError(error);
    $("materialMessage").className = "message error";
  }
}

async function clearLock(event) {
  event.preventDefault();
  if (!state.activeLock) {
    return;
  }

  try {
    await api(`/alerts/${state.activeLock.alertId}/override`, {
      method: "POST",
      body: JSON.stringify({
        overrideUserName: $("overrideUserName").value.trim(),
        overridePassword: $("overridePassword").value,
        causeText: $("causeText").value.trim(),
        solutionText: $("solutionText").value.trim(),
        whyStandardProcessWasBypassed: $("bypassReason").value.trim() || null,
        unlockedAt: new Date().toISOString()
      })
    });
    $("overridePassword").value = "";
    $("causeText").value = "";
    $("solutionText").value = "";
    $("bypassReason").value = "";
    $("overrideMessage").textContent = "Lock cleared.";
    $("overrideMessage").className = "message ok";
    await loadContext();
  } catch (error) {
    $("overrideMessage").textContent = readableError(error);
    $("overrideMessage").className = "message error";
  }
}

function canCurrentUserOverride() {
  return state.user?.permissions?.includes("CanOverrideDriftLock") === true;
}

function canManageSetup() {
  return state.user?.permissions?.some((permission) =>
    permission === "CanManageInspectionPlans" ||
    permission === "CanImportSetupData" ||
    permission === "CanManageUsers") === true;
}

function showPanel(panelName) {
  const showSetup = panelName === "setup";
  $("workPanel").classList.toggle("hidden", showSetup);
  $("setupPanel").classList.toggle("hidden", !showSetup);
  $("inspectionTab").classList.toggle("active", !showSetup);
  $("setupTab").classList.toggle("active", showSetup);
}

function logout() {
  state.user = null;
  state.snapshot = null;
  state.contexts = [];
  state.selectedPlans = [];
  state.trendCharacteristic = "";
  state.activeLock = null;
  state.users = [];
  state.roles = [];
  setStatus($("userBadge"), "Not signed in");
  $("logoutButton").classList.add("hidden");
  $("navTabs").classList.add("hidden");
  $("loginPanel").classList.remove("hidden");
  $("workPanel").classList.add("hidden");
  $("setupPanel").classList.add("hidden");
  $("inspectionTab").classList.add("active");
  $("setupTab").classList.remove("active");
  $("password").value = "";
  renderEmptyContext();
}

async function loadSetupAdmin() {
  state.roles = await api("/setup/roles");
  state.users = await api("/setup/users");
  fillSelect($("setupRole"), state.roles, (role) => role, (role) => role);
  renderUsers();
}

function renderPartReviewControls() {
  const parts = [{ partNum: "", description: "All parts" }, ...state.snapshot.parts];
  fillSelect($("partReviewFilter"), parts, (part) => part.partNum, (part) => part.partNum || part.description);
}

function renderPartReview() {
  const container = $("partReviewList");
  const selectedPart = $("partReviewFilter").value;
  const plans = state.snapshot.inspectionPlans
    .filter((plan) => !selectedPart || plan.partNum === selectedPart)
    .sort((a, b) =>
      a.partNum.localeCompare(b.partNum) ||
      a.operationSeq - b.operationSeq ||
      a.characteristicName.localeCompare(b.characteristicName));

  if (!plans.length) {
    container.className = "data-table empty";
    container.textContent = "No variables configured.";
    return;
  }

  container.className = "data-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Part</span><span>Operation</span><span>Variable</span><span>Spec</span><span>COA</span>
    </div>`;
  plans.forEach((plan) => {
    const row = document.createElement("div");
    row.className = "data-row";
    row.innerHTML = `
      <span>${plan.partNum}</span>
      <span>${plan.processCode} ${plan.operationSeq}</span>
      <span>${plan.characteristicName} (${plan.unitOfMeasure})</span>
      <span>${formatNumber(plan.lsl)} / ${formatNumber(plan.nominal)} / ${formatNumber(plan.usl)}</span>
      <span>${plan.isRequiredForCoa ? "Required" : "No"}</span>`;
    container.appendChild(row);
  });
}

async function loadJobSummary(event) {
  event?.preventDefault();
  const jobNums = parseJobNums();
  if (!jobNums.length) {
    $("jobSummaryMessage").textContent = "Enter at least one job number.";
    $("jobSummaryMessage").className = "message error";
    return;
  }

  try {
    const requiredOnly = $("summaryRequiredOnly").value;
    const rows = await api(`/qa/job-variable-means?jobNums=${encodeURIComponent(jobNums.join(","))}&requiredOnly=${requiredOnly}`);
    renderJobSummary(rows);
    $("jobSummaryMessage").textContent = `${rows.length} variable${rows.length === 1 ? "" : "s"} loaded.`;
    $("jobSummaryMessage").className = "message ok";
  } catch (error) {
    $("jobSummaryMessage").textContent = readableError(error);
    $("jobSummaryMessage").className = "message error";
  }
}

function renderJobSummary(rows) {
  const container = $("jobSummaryList");
  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = "No variables found.";
    return;
  }

  container.className = "data-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Job</span><span>Variable</span><span>Mean</span><span>Count</span><span>Status</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.characteristicName} (${row.unitOfMeasure})</span>
      <span>${formatNumber(row.mean)}</span>
      <span>${row.count}</span>
      <span>${row.status}${row.isRequiredForCoa ? " / COA" : ""}</span>`;
    container.appendChild(item);
  });
}

function openJobSummaryCsv() {
  const jobNums = parseJobNums();
  if (!jobNums.length) {
    $("jobSummaryMessage").textContent = "Enter at least one job number.";
    $("jobSummaryMessage").className = "message error";
    return;
  }
  const requiredOnly = $("summaryRequiredOnly").value;
  window.open(`/qa/job-variable-means.csv?jobNums=${encodeURIComponent(jobNums.join(","))}&requiredOnly=${requiredOnly}`, "_blank");
}

function parseJobNums() {
  return $("summaryJobNum").value
    .split(",")
    .map((jobNum) => jobNum.trim())
    .filter(Boolean);
}

function renderUsers() {
  const list = $("userList");
  if (!state.users.length) {
    list.className = "setup-list empty";
    list.textContent = "No users loaded.";
    return;
  }

  list.className = "setup-list";
  list.innerHTML = "";
  state.users.forEach((user) => {
    const row = document.createElement("div");
    row.className = "setup-row";
    row.innerHTML = `<div><strong>${user.userName}</strong><span>${user.roles.join(", ")}</span></div><button type="button" class="secondary">Edit</button>`;
    row.querySelector("button").addEventListener("click", () => {
      $("setupUserName").value = user.userName;
      $("setupPassword").value = "";
      $("setupRole").value = user.roles[0] || state.roles[0] || "";
    });
    list.appendChild(row);
  });
}

async function saveUser(event) {
  event.preventDefault();
  try {
    await api("/setup/users", {
      method: "POST",
      body: JSON.stringify({
        userName: $("setupUserName").value.trim(),
        password: $("setupPassword").value,
        roles: [$("setupRole").value]
      })
    });
    $("setupPassword").value = "";
    $("userSetupMessage").textContent = "User saved.";
    $("userSetupMessage").className = "message ok";
    await loadSetupAdmin();
  } catch (error) {
    $("userSetupMessage").textContent = readableError(error);
    $("userSetupMessage").className = "message error";
  }
}

async function saveInspectionSetup(event) {
  event.preventDefault();
  try {
    await api("/setup/inspection-plans", {
      method: "POST",
      body: JSON.stringify({
        partNum: $("setupPartNum").value.trim(),
        partDescription: $("setupPartDescription").value.trim(),
        processCode: $("setupProcessCode").value.trim(),
        processDescription: $("setupProcessDescription").value.trim(),
        operationSeq: Number($("setupOperationSeq").value),
        characteristicName: $("setupCharacteristicName").value.trim(),
        characteristicType: $("setupCharacteristicType").value,
        nominal: Number($("setupNominal").value),
        lsl: Number($("setupLsl").value),
        usl: Number($("setupUsl").value),
        lcl: optionalNumber("setupLcl"),
        ucl: optionalNumber("setupUcl"),
        unitOfMeasure: $("setupUnitOfMeasure").value.trim(),
        sampleSize: Number($("setupSampleSize").value),
        frequencyType: $("setupFrequencyType").value,
        frequencyValue: Number($("setupFrequencyValue").value),
        frequencyUnit: $("setupFrequencyUnit").value,
        alertRuleSet: "WesternElectric",
        isRequiredForCoa: $("setupIsRequiredForCoa").value === "true"
      })
    });
    $("inspectionSetupMessage").textContent = "Measurement setup saved.";
    $("inspectionSetupMessage").className = "message ok";
    await loadSnapshot();
  } catch (error) {
    $("inspectionSetupMessage").textContent = readableError(error);
    $("inspectionSetupMessage").className = "message error";
  }
}

async function importCsv(event) {
  event.preventDefault();
  try {
    await api("/setup/import-csv", {
      method: "POST",
      body: JSON.stringify({ csv: $("csvImportText").value })
    });
    $("csvImportMessage").textContent = "CSV imported.";
    $("csvImportMessage").className = "message ok";
    await loadSnapshot();
  } catch (error) {
    $("csvImportMessage").textContent = readableError(error);
    $("csvImportMessage").className = "message error";
  }
}

function optionalNumber(id) {
  const value = $(id).value.trim();
  return value ? Number(value) : null;
}

function newClientRecordId() {
  if (window.crypto?.randomUUID) {
    return window.crypto.randomUUID();
  }

  const random = window.crypto?.getRandomValues
    ? Array.from(window.crypto.getRandomValues(new Uint32Array(4)), (value) => value.toString(16).padStart(8, "0")).join("")
    : Math.random().toString(16).slice(2) + Math.random().toString(16).slice(2);
  return `${Date.now().toString(16)}-${random}`;
}

function loadCsvTemplate() {
  $("csvImportText").value = [
    "PartNum,PartDescription,ProcessCode,ProcessDescription,OperationSeq,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA",
    "P200,Example part,MOLD,Molding,10,Measurement 1,Variable,5.0,4.5,5.5,4.4,5.6,mm,1,Time,30,Minutes,WesternElectric,true",
    "P200,Example part,MOLD,Molding,10,Measurement 2,Variable,42.0,41.5,42.5,41.0,43.0,mm,1,Time,30,Minutes,WesternElectric,true"
  ].join("\n");
}

function readableError(error) {
  try {
    const parsed = JSON.parse(error.message);
    return parsed.errors?.join(" ") || error.message;
  } catch {
    return error.message;
  }
}

function formatNumber(value) {
  return value === null || value === undefined ? "-" : Number(value).toFixed(3);
}

function formatTime(value) {
  return new Date(value).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function ruleLabel(rule) {
  return {
    OnePointBeyondControlLimit: "One point beyond control limit",
    TwoOfThreeNearControlLimit: "Two of three near control limit",
    FourOfFiveApproachingLimit: "Four of five approaching limit",
    EightConsecutiveOneSideOfCenterline: "Eight consecutive one side of centerline"
  }[rule] || rule;
}

window.addEventListener("online", () => setStatus($("syncStatus"), "Online", "ok"));
window.addEventListener("offline", () => setStatus($("syncStatus"), "Offline", "warn"));
$("loginForm").addEventListener("submit", login);
$("contextForm").addEventListener("submit", loadContext);
$("jobNum").addEventListener("change", loadContext);
$("partNum").addEventListener("change", loadContext);
$("resourceId").addEventListener("change", loadContext);
$("logoutButton").addEventListener("click", logout);
$("measurementForm").addEventListener("submit", submitMeasurement);
$("materialForm").addEventListener("submit", saveMaterialChange);
$("overrideForm").addEventListener("submit", clearLock);
$("overrideUserName").addEventListener("input", () => {
  $("godReasonLabel").classList.toggle("hidden", $("overrideUserName").value.trim().toLowerCase() !== "god1");
});
$("trendCharacteristic").addEventListener("change", () => {
  state.trendCharacteristic = $("trendCharacteristic").value;
  loadTrend();
});
$("inspectionTab").addEventListener("click", () => showPanel("inspect"));
$("setupTab").addEventListener("click", () => showPanel("setup"));
$("userSetupForm").addEventListener("submit", saveUser);
$("inspectionSetupForm").addEventListener("submit", saveInspectionSetup);
$("csvImportForm").addEventListener("submit", importCsv);
$("csvTemplateButton").addEventListener("click", loadCsvTemplate);
$("partReviewFilter").addEventListener("change", renderPartReview);
$("jobSummaryForm").addEventListener("submit", loadJobSummary);
$("jobSummaryCsvButton").addEventListener("click", openJobSummaryCsv);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}

const state = {
  user: null,
  snapshot: null,
  contexts: [],
  selectedPlans: [],
  jobNotes: [],
  trendCharacteristic: "",
  trendChartType: "Individuals",
  activeLock: null,
  users: [],
  roles: [],
  editingSetup: null
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
  if (response.status === 204) {
    return null;
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
    const key = `${plan.partNum}|${plan.processCode}|${plan.operationSeq}|${plan.inspectionPhase || "In Process"}`;
    if (!map.has(key)) {
      map.set(key, {
        key,
        partNum: plan.partNum,
        partDescription: plan.partDescription,
        processCode: plan.processCode,
        processDescription: plan.processDescription,
        operationSeq: plan.operationSeq,
        inspectionPhase: plan.inspectionPhase || "In Process",
        plans: []
      });
    }
    map.get(key).plans.push(plan);
  });
  return [...map.values()];
}

function selectedInspectionSet() {
  const partNum = $("partNum").value.trim();
  const phase = $("inspectionPhase").value;
  return inspectionSets().find((set) =>
    set.partNum.toLowerCase() === partNum.toLowerCase() &&
    normalizeInspectionPhase(set.inspectionPhase) === normalizeInspectionPhase(phase)) || null;
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
  document.body.classList.remove("login-active");
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
  fillDatalist($("jobOptions"), state.snapshot.jobs, (job) => job.jobNum);
  $("jobNum").value = "";
  fillSelect($("resourceId"), [{ resourceId: "", description: "Select machine" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillDatalist($("partOptions"), state.snapshot.parts, (part) => part.partNum);
  $("partNum").value = "";
  if (canManageSetup()) {
    renderGlobalRuleSetting();
    renderPartReviewControls();
    renderSetupEditChoices();
    renderPartReview();
  }
  clearWorkContext();
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

function normalizeInspectionPhase(value) {
  if (!value) return "In Process";
  const phase = value.trim().toLowerCase();
  if (phase === "startup") return "Startup";
  if (phase === "set up" || phase === "setup") return "Setup";
  return "In Process";
}

function updatePartFromJob() {
  const job = state.snapshot.jobs.find((item) => item.jobNum.toLowerCase() === $("jobNum").value.trim().toLowerCase());
  if (job && state.snapshot.parts.some((part) => part.partNum.toLowerCase() === job.partNum.toLowerCase())) {
    $("partNum").value = job.partNum;
  }
}

async function loadContext(event) {
  event?.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  const partNum = $("partNum").value.trim();
  if (!jobNum || !resourceId || !partNum) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext();
    return;
  }

  if (!set) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext(`Part ${partNum} is not set up for ${$("inspectionPhase").value} inspections. Ask Admin or GOD to add that inspection phase before inspecting.`);
    return;
  }

  state.selectedPlans = set.plans;
  state.contexts = await Promise.all(set.plans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  renderContext();
}

function renderEmptyContext(message = "") {
  $("contextTitle").textContent = "Select work to begin";
  $("contextSubtitle").textContent = "Enter a job number, machine, and part number, then start inspecting.";
  renderLock(null);
  $("measurementForm").classList.add("hidden");
  $("trendPanel").classList.add("hidden");
  $("jobNotesPanel").classList.add("hidden");
  $("materialDivider").classList.add("hidden");
  $("materialSection").classList.add("hidden");
  $("variableList").innerHTML = "";
  $("meanSummary").innerHTML = "";
  $("trendCharacteristic").innerHTML = "";
  $("entryMessage").textContent = message;
  $("entryMessage").className = message ? "message error" : "message";
  $("materialMessage").textContent = "";
  $("jobNoteText").value = "";
  $("jobNoteMessage").textContent = "";
  renderJobNotes([]);
  drawTrend([]);
}

async function loadVariableContext(jobNum, resourceId, plan) {
  const params = new URLSearchParams({
    jobNum,
    partNum: plan.partNum,
    processCode: plan.processCode,
    operationSeq: String(plan.operationSeq),
    resourceId,
    characteristicName: plan.characteristicName,
    inspectionPhase: plan.inspectionPhase || $("inspectionPhase").value
  });
  return api(`/work-context?${params}`);
}

function renderContext() {
  const { jobNum, resourceId, set } = selectedValues();
  $("contextTitle").textContent = `${jobNum} ${resourceId}`;
  $("contextSubtitle").textContent = `${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.inspectionPhase}`;
  $("measurementForm").classList.remove("hidden");
  $("trendPanel").classList.remove("hidden");
  $("jobNotesPanel").classList.remove("hidden");
  $("materialDivider").classList.remove("hidden");
  $("materialSection").classList.remove("hidden");
  state.activeLock = state.contexts.find((context) => context.activeLock)?.activeLock || null;
  renderLock(state.activeLock);
  renderVariables();
  renderMeanSummary();
  renderTrendChoices();
  loadTrend();
  loadJobNotes(jobNum);
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
  banner.textContent = `LOCKED: ${activeLock.characteristicName} - ${ruleLabel(activeLock.ruleTriggered)} at ${formatTime(activeLock.lockedAt)}`;
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
    const isAttribute = plan.characteristicType === "Attribute";
    card.innerHTML = `
      <div>
        <div class="variable-header">
          <div class="variable-title">
            <strong>${plan.characteristicName}</strong>
            <span>${isAttribute ? "Accept / Reject" : plan.unitOfMeasure}</span>
          </div>
          <div class="sample-meta">
            <span>${plan.inspectionPhase || "In Process"}</span>
            <span>Sample size ${plan.sampleSize}</span>
            <span>${formatFrequency(plan)}</span>
          </div>
        </div>
        ${isAttribute ? `
          <div class="attribute-note">Comparator/template check</div>` : `
          <div class="limit-grid">
            <div><span>LSL</span><strong>${formatNumber(context.lowerSpecLimit)}</strong></div>
            <div><span>Target</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>USL</span><strong>${formatNumber(context.upperSpecLimit)}</strong></div>
            <div><span>LCL</span><strong>${formatNumber(context.lowerControlLimit)}</strong></div>
            <div><span>Center</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>UCL</span><strong>${formatNumber(context.upperControlLimit)}</strong></div>
          </div>`}
      </div>
      <div class="sample-inputs">
        ${Array.from({ length: plan.sampleSize }, (_, sampleIndex) => `
          <label>
            Sample ${sampleIndex + 1}
            ${isAttribute ? `
              <select class="measurement-input" data-plan-index="${index}" data-sample-index="${sampleIndex}" data-entry-type="Attribute">
                <option value="">Select</option>
                <option value="1">Accept</option>
                <option value="0">Reject</option>
              </select>` : `
              <input class="measurement-input" data-plan-index="${index}" data-sample-index="${sampleIndex}" data-entry-type="Variable" type="number" step="0.0001" inputmode="decimal" placeholder="0.0000">`}
          </label>`).join("")}
      </div>`;
    list.appendChild(card);
  });
}

function renderMeanSummary() {
  const summary = $("meanSummary");
  summary.innerHTML = "";
  if (!state.selectedPlans.length) {
    summary.className = "mean-summary empty";
    return;
  }

  summary.className = "mean-summary capability-table";
  summary.innerHTML = `
    <div class="capability-row capability-header">
      <span>Variable</span>
      <span>Mean</span>
      <span>Cp</span>
      <span>Cpk</span>
      <span>Pp</span>
      <span>Ppk</span>
      <span>Points</span>
    </div>`;
  state.selectedPlans.forEach((plan, index) => {
    const points = state.contexts[index]?.recentMeasurements || [];
    const mean = points.length
      ? points.reduce((total, point) => total + Number(point.value), 0) / points.length
      : null;
    const item = document.createElement("div");
    item.className = "capability-row";
    if (plan.characteristicType === "Attribute") {
      const accepted = points.filter((point) => Number(point.value) === 1).length;
      item.innerHTML = `
        <span>${plan.characteristicName}</span>
        <span>${accepted}/${points.length || 0}</span>
        <span class="muted-cell">Accept/Reject</span>
        <span class="muted-cell">-</span>
        <span class="muted-cell">-</span>
        <span class="muted-cell">-</span>
        <span>${points.length}</span>`;
      summary.appendChild(item);
      return;
    }
    const capability = state.contexts[index]?.capability || {};
    item.innerHTML = `
      <span>${plan.characteristicName}</span>
      <span>${formatNumber(mean)}</span>
      <span>${capabilityBadge(capability.cp)}</span>
      <span>${capabilityBadge(capability.cpk)}</span>
      <span>${capabilityBadge(capability.pp)}</span>
      <span>${capabilityBadge(capability.ppk)}</span>
      <span>${points.length}</span>`;
    summary.appendChild(item);
  });
}

function capabilityBadge(value) {
  return `<span class="capability-chip ${capabilityClass(value)}">${formatNumber(value)}</span>`;
}

function capabilityClass(value) {
  if (value === null || value === undefined || !Number.isFinite(Number(value))) return "capability-neutral";
  if (Number(value) >= 1.33) return "capability-good";
  if (Number(value) >= 1.0) return "capability-warn";
  return "capability-bad";
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
          inspectionPhase: entry.plan.inspectionPhase || $("inspectionPhase").value,
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
  state.trendChartType = $("trendChartType").value;
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
      to: null,
      inspectionPhase: set.inspectionPhase || $("inspectionPhase").value
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

  const chartType = state.trendChartType || "Individuals";
  $("trendMessage").textContent = `${points.length} point${points.length === 1 ? "" : "s"} for ${state.trendCharacteristic} / ${chartTypeLabel(chartType)}`;
  const min = Math.min(...values, ...limitValues);
  const max = Math.max(...values, ...limitValues);
  const spread = max === min ? 1 : max - min;
  const low = min - spread * 0.1;
  const high = max + spread * 0.1;
  const x = (index) => padding.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
  const y = (value) => padding.top + (1 - ((Number(value) - low) / (high - low))) * plotHeight;

  drawChartFrame(ctx, padding, plotWidth, plotHeight);
  if (chartType === "Histogram") {
    drawHistogram(ctx, points, padding, plotWidth, plotHeight, low, high);
  } else if (chartType === "MovingRange") {
    drawMovingRange(ctx, points, padding, plotWidth, plotHeight);
  } else {
    if (chartType === "ControlLimits") {
      drawLimitLine(ctx, y, data.upperControlLimit, "UCL", "#c76508", width, padding);
      drawLimitLine(ctx, y, data.lowerControlLimit, "LCL", "#c76508", width, padding);
      drawLimitLine(ctx, y, data.upperSpecLimit, "USL", "#b42318", width, padding);
      drawLimitLine(ctx, y, data.lowerSpecLimit, "LSL", "#b42318", width, padding);
    }
    if (chartType === "Run") {
      drawLimitLine(ctx, y, data.mean, "Mean", "#067647", width, padding);
    }
    drawLineSeries(ctx, points, (point, index) => x(index), (point) => y(point.value));
  }

  ctx.fillStyle = "#5f6f82";
  ctx.font = "12px Segoe UI, Arial";
  ctx.fillText(formatNumber(low), 6, padding.top + plotHeight);
  ctx.fillText(formatNumber(high), 6, padding.top + 8);
}

function drawLineSeries(ctx, points, xOf, yOf) {
  ctx.strokeStyle = "#0f63b8";
  ctx.lineWidth = 2;
  ctx.beginPath();
  points.forEach((point, index) => {
    const x = xOf(point, index);
    const y = yOf(point, index);
    if (index === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  });
  ctx.stroke();

  points.forEach((point, index) => {
    ctx.beginPath();
    ctx.fillStyle = point.hasRuleViolation ? "#b42318" : "#0f63b8";
    ctx.arc(xOf(point, index), yOf(point, index), 4, 0, Math.PI * 2);
    ctx.fill();
  });
}

function drawMovingRange(ctx, points, padding, plotWidth, plotHeight) {
  const rangePoints = points
    .map((point) => ({ ...point, rangeValue: Number(point.movingRange) }))
    .filter((point) => Number.isFinite(point.rangeValue));
  if (!rangePoints.length) return;
  const maxRange = Math.max(...rangePoints.map((point) => point.rangeValue), 1);
  const x = (_, index) => padding.left + (rangePoints.length === 1 ? plotWidth / 2 : (index / (rangePoints.length - 1)) * plotWidth);
  const y = (point) => padding.top + (1 - (point.rangeValue / (maxRange * 1.1))) * plotHeight;
  drawLineSeries(ctx, rangePoints, x, y);
}

function drawHistogram(ctx, points, padding, plotWidth, plotHeight, low, high) {
  const values = points.map((point) => Number(point.value));
  const binCount = Math.min(8, Math.max(4, Math.ceil(Math.sqrt(values.length))));
  const binWidth = (high - low) / binCount || 1;
  const bins = Array.from({ length: binCount }, () => 0);
  values.forEach((value) => {
    const index = Math.min(binCount - 1, Math.max(0, Math.floor((value - low) / binWidth)));
    bins[index] += 1;
  });
  const maxBin = Math.max(...bins, 1);
  const barGap = 5;
  const barWidth = plotWidth / binCount - barGap;
  bins.forEach((count, index) => {
    const height = (count / maxBin) * plotHeight;
    const x = padding.left + index * (plotWidth / binCount) + barGap / 2;
    const y = padding.top + plotHeight - height;
    ctx.fillStyle = "#0f63b8";
    ctx.fillRect(x, y, Math.max(4, barWidth), height);
  });
}

function chartTypeLabel(value) {
  return {
    Individuals: "Individuals",
    MovingRange: "Moving range",
    Run: "Run chart",
    Histogram: "Histogram",
    ControlLimits: "Control / spec limits",
    GlobalDefault: "Use Global Default",
    WesternElectric: "Western Electric",
    NelsonRules: "Nelson Rules",
    Cusum: "CUSUM",
    Ewma: "EWMA",
    MovingAverageTrend: "Moving Average Trend",
    LinearTrendSlope: "Linear Trend / Slope",
    Custom: "Custom Rule",
    SpecLimitOnly: "Spec Limit Only",
    None: "No Automatic Rule"
  }[value] || value;
}

function drawChartFrame(ctx, padding, plotWidth, plotHeight) {
  ctx.strokeStyle = "#d7e1ec";
  ctx.lineWidth = 1;
  ctx.strokeRect(padding.left, padding.top, plotWidth, plotHeight);
  ctx.strokeStyle = "#edf4fb";
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
  if (!jobNum || !resourceId || !set) {
    $("materialMessage").textContent = "Start work before saving a material event.";
    $("materialMessage").className = "message error";
    return;
  }

  try {
    await api("/material-changes", {
      method: "POST",
      body: JSON.stringify({
        jobNum,
        partNum: set.partNum,
        materialPartNum: $("materialPartNum").value.trim(),
        oldLotNum: "",
        newLotNum: $("newLotNum").value.trim(),
        quantityLoaded: null,
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
    $("materialMessage").textContent = "Material event saved.";
    $("materialMessage").className = "message ok";
    await loadJobNotes(jobNum);
  } catch (error) {
    $("materialMessage").textContent = readableError(error);
    $("materialMessage").className = "message error";
  }
}

async function loadJobNotes(jobNum) {
  if (!jobNum) {
    renderJobNotes([]);
    return;
  }

  try {
    const history = await api(`/jobs/${encodeURIComponent(jobNum)}/history`);
    state.jobNotes = history;
    renderJobNotes(history);
  } catch (error) {
    $("jobNoteMessage").textContent = readableError(error);
    $("jobNoteMessage").className = "message error";
  }
}

async function saveJobNote(event) {
  event.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  const noteText = $("jobNoteText").value.trim();
  if (!jobNum || !resourceId || !set) {
    $("jobNoteMessage").textContent = "Start work before saving a job note.";
    $("jobNoteMessage").className = "message error";
    return;
  }

  if (!noteText) {
    $("jobNoteMessage").textContent = "Enter a note before submitting.";
    $("jobNoteMessage").className = "message error";
    return;
  }

  try {
    await api(`/jobs/${encodeURIComponent(jobNum)}/notes`, {
      method: "POST",
      body: JSON.stringify({
        jobNum,
        partNum: set.partNum,
        resourceId,
        operatorUserId: state.user.userName,
        noteText,
        timestamp: new Date().toISOString()
      })
    });
    $("jobNoteText").value = "";
    $("jobNoteMessage").textContent = "Job note saved.";
    $("jobNoteMessage").className = "message ok";
    await loadJobNotes(jobNum);
  } catch (error) {
    $("jobNoteMessage").textContent = readableError(error);
    $("jobNoteMessage").className = "message error";
  }
}

function renderJobNotes(entries) {
  renderHistoryList($("jobNoteList"), entries);
}

function renderHistoryList(list, entries) {
  if (!entries.length) {
    list.className = "job-note-list empty";
    list.textContent = "No history for this job yet.";
    return;
  }

  list.className = "job-note-list";
  list.innerHTML = "";
  entries.forEach((entry) => {
    const item = document.createElement("article");
    item.className = `job-note-item ${entry.entryType === "Lock" ? "lock-history-item" : entry.entryType === "Material" ? "material-history-item" : ""}`;
    const meta = document.createElement("div");
    meta.className = "job-note-meta";
    const user = document.createElement("strong");
    user.textContent = historyEntryTitle(entry);
    const details = document.createElement("span");
    details.textContent = `${formatDateTime(entry.timestamp)} / ${entry.partNum} / ${entry.resourceId}`;
    const text = document.createElement("p");
    if (entry.entryType === "Lock") {
      text.textContent = lockHistoryText(entry);
    } else if (entry.entryType === "Material") {
      text.textContent = materialHistoryText(entry);
    } else {
      text.textContent = entry.noteText;
    }
    meta.append(user, details);
    item.append(meta, text);
    list.appendChild(item);
  });
}

function historyEntryTitle(entry) {
  if (entry.entryType === "Lock") {
    return `${entry.characteristicName} ${entry.status === "Active" ? "locked" : "lock cleared"}`;
  }

  if (entry.entryType === "Material") {
    return `Material ${entry.reason || "event"}`;
  }

  return entry.operatorUserId;
}

function lockHistoryText(entry) {
  const parts = [`${ruleLabel(entry.ruleTriggered)}.`];
  if (entry.status === "Active") {
    parts.push("This lock is still active.");
  }

  if (entry.overrideUserId) {
    parts.push(`Cleared by ${entry.overrideUserId}${entry.overrideRole ? ` (${entry.overrideRole})` : ""} at ${formatDateTime(entry.unlockedAt)}.`);
  }

  if (entry.causeCategory || entry.causeText) {
    parts.push(`Cause: ${[entry.causeCategory, entry.causeText].filter(Boolean).join(" - ")}.`);
  }

  if (entry.solutionText) {
    parts.push(`Action: ${entry.solutionText}.`);
  }

  return parts.join(" ");
}

function materialHistoryText(entry) {
  const parts = [`${entry.materialPartNum || "Material"} lot ${entry.newLotNum || "-"}.`];
  if (entry.quantityLoaded !== null && entry.quantityLoaded !== undefined) {
    parts.push(`Quantity ${formatNumber(entry.quantityLoaded)}.`);
  }
  parts.push(`Recorded by ${entry.operatorUserId}.`);
  return parts.join(" ");
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
        causeCategory: $("causeCategory").value,
        causeText: $("causeText").value.trim(),
        solutionText: $("solutionText").value.trim(),
        whyStandardProcessWasBypassed: $("bypassReason").value.trim() || null,
        unlockedAt: new Date().toISOString()
      })
    });
    $("overridePassword").value = "";
    $("causeCategory").value = "Machine";
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

function showSetupSection(sectionName) {
  const sections = ["Inspection", "Users", "Rules", "Import", "Review", "JobData"];
  sections.forEach((section) => {
    $(`setup${section}Section`).classList.toggle("hidden", section !== sectionName);
    $(`setup${section}SectionTab`).classList.toggle("active", section === sectionName);
  });
}

function logout() {
  state.user = null;
  state.snapshot = null;
  state.contexts = [];
  state.selectedPlans = [];
  state.jobNotes = [];
  state.trendCharacteristic = "";
  state.activeLock = null;
  state.users = [];
  state.roles = [];
  document.body.classList.add("login-active");
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

function clearWorkContext() {
  state.contexts = [];
  state.selectedPlans = [];
  state.jobNotes = [];
  state.trendCharacteristic = "";
  state.activeLock = null;
  renderEmptyContext();
}

async function loadSetupAdmin() {
  state.roles = await api("/setup/roles");
  state.users = await api("/setup/users");
  fillSelect($("setupRole"), state.roles, (role) => role, (role) => role);
  renderUsers();
  if (!$("setupVariableRows").children.length) {
    addSetupVariableRow();
  }
}

function renderPartReviewControls() {
  const parts = [{ partNum: "", description: "All parts" }, ...state.snapshot.parts];
  fillSelect($("partReviewFilter"), parts, (part) => part.partNum, (part) => part.partNum || part.description);
}

async function loadReview() {
  const partNum = $("partReviewFilter").value;
  const jobNum = $("reviewJobNum").value.trim();
  if (!jobNum) {
    $("partReviewList").classList.remove("hidden");
    await renderPartReview();
    $("jobReviewPanel").classList.add("hidden");
    $("reviewMessage").textContent = "";
    $("reviewMessage").className = "message";
    return;
  }

  if (!partNum) {
    $("reviewMessage").textContent = "Select a part before reviewing a job.";
    $("reviewMessage").className = "message error";
    $("jobReviewPanel").classList.add("hidden");
    return;
  }

  try {
    $("partReviewList").classList.add("hidden");
    const review = await api(`/review/job?partNum=${encodeURIComponent(partNum)}&jobNum=${encodeURIComponent(jobNum)}`);
    renderJobReview(review);
    $("reviewMessage").textContent = `${jobNum} review loaded.`;
    $("reviewMessage").className = "message ok";
  } catch (error) {
    $("reviewMessage").textContent = readableError(error);
    $("reviewMessage").className = "message error";
    $("jobReviewPanel").classList.add("hidden");
  }
}

function renderSetupEditChoices() {
  const sets = [{ key: "", label: "Create new part setup" }, ...inspectionSets().map((set) => ({
    key: set.key,
    label: `${set.partNum} / ${set.processCode} / ${set.inspectionPhase}`
  }))];
  fillSelect($("setupEditPartSelect"), sets, (set) => set.key, (set) => set.label);
}

function renderGlobalRuleSetting() {
  $("globalAlertRuleSet").value = state.snapshot.settings?.globalAlertRuleSet || "WesternElectric";
  updateRuleDescription();
}

async function renderPartReview() {
  const container = $("partReviewList");
  const selectedPart = $("partReviewFilter").value;
  if (!selectedPart) {
    container.className = "data-table empty";
    container.textContent = "Select a part to review capability across all jobs.";
    return;
  }

  try {
    const rows = await api(`/review/part?partNum=${encodeURIComponent(selectedPart)}`);
    renderReviewSummary(rows, container, "No measured-variable data for this part.");
  } catch (error) {
    container.className = "data-table empty";
    container.textContent = readableError(error);
  }
}

function renderJobReview(review) {
  $("jobReviewPanel").classList.remove("hidden");
  renderReviewSummary(review.variableSummary || [], $("jobReviewSummary"), "No summary data for this job.");
  renderReviewMeasurements(review.measurements || []);
  renderHistoryList($("jobReviewHistory"), review.history || []);
}

function renderReviewSummary(rows, container, emptyMessage) {
  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = emptyMessage;
    return;
  }

  container.className = "data-table review-summary-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Scope</span><span>Variable</span><span>Type</span><span>Mean</span><span>Std Dev</span><span>Cp</span><span>Cpk</span><span>Pp</span><span>Ppk</span><span>Count</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.characteristicName}</span>
      <span>${row.characteristicType === "Attribute" ? "Accept/Reject" : "Measured"}</span>
      <span>${formatNumber(row.mean)}</span>
      <span>${formatNumber(row.stdDev)}</span>
      <span>${capabilityBadge(row.cp)}</span>
      <span>${capabilityBadge(row.cpk)}</span>
      <span>${capabilityBadge(row.pp)}</span>
      <span>${capabilityBadge(row.ppk)}</span>
      <span>${row.count}${row.outOfSpecExcludedCount ? ` / ${row.outOfSpecExcludedCount} excluded` : ""}</span>`;
    container.appendChild(item);
  });
}

async function saveReviewMeasurement(id, item) {
  const row = item.closest(".data-row");
  const value = Number(row.querySelector(".review-measurement-value").value);
  const inspectionPhase = row.querySelector(".review-measurement-phase").value;
  if (!Number.isFinite(value)) {
    $("reviewMessage").textContent = "Measurement value must be numeric.";
    $("reviewMessage").className = "message error";
    return;
  }

  try {
    await api(`/review/measurements/${id}`, {
      method: "PATCH",
      body: JSON.stringify({ value, inspectionPhase })
    });
    $("reviewMessage").textContent = "Inspection entry updated.";
    $("reviewMessage").className = "message ok";
    await loadReview();
  } catch (error) {
    $("reviewMessage").textContent = readableError(error);
    $("reviewMessage").className = "message error";
  }
}

function renderReviewMeasurements(rows) {
  const container = $("jobReviewMeasurements");
  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = "No measurements for this job.";
    return;
  }

  container.className = "data-table review-measurement-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Time</span><span>Phase</span><span>Variable</span><span>Value</span><span>Machine</span><span>Operation</span><span>User</span><span></span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = `data-row ${row.isOutOfSpec ? "measurement-out-spec" : row.isOutOfControl ? "measurement-out-control" : ""}`;
    item.innerHTML = `
      <span>${formatDateTime(row.timestamp)}</span>
      <span>
        <select class="review-measurement-phase">
          <option value="Startup" ${row.inspectionPhase === "Startup" ? "selected" : ""}>Startup</option>
          <option value="Setup" ${row.inspectionPhase === "Setup" ? "selected" : ""}>Setup</option>
          <option value="In Process" ${row.inspectionPhase === "In Process" ? "selected" : ""}>In Process</option>
        </select>
      </span>
      <span>${row.characteristicName}</span>
      <span><input class="review-measurement-value" type="number" step="0.0001" value="${Number(row.value)}"></span>
      <span>${row.resourceId}${row.isOutOfSpec ? ` <strong class="status-text bad">Out of spec</strong>` : row.isOutOfControl ? ` <strong class="status-text warn">Out of control</strong>` : ""}</span>
      <span>${row.processCode} ${row.operationSeq}</span>
      <span>${row.operatorUserId}</span>
      <span><button type="button" class="secondary compact-button">Save</button></span>`;
    item.querySelector("button").addEventListener("click", () => saveReviewMeasurement(row.id, item));
    container.appendChild(item);
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

  container.className = "data-table job-summary-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Job</span><span>Variable</span><span>COA Stat</span><span>COA Value</span><span>Mean</span><span>Std Dev</span><span>Cpk</span><span>Ppk</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.characteristicName} (${row.unitOfMeasure})</span>
      <span>${coaStatisticLabel(row.coaStatisticType)}</span>
      <span>${formatNumber(row.coaValue)}</span>
      <span>${formatNumber(row.mean)}</span>
      <span>${formatNumber(row.stdDev)}</span>
      <span>${capabilityBadge(row.cpk)}</span>
      <span>${capabilityBadge(row.ppk)}</span>`;
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

function coaStatisticLabel(value) {
  return {
    Mean: "Mean",
    StandardDeviation: "Std dev"
  }[value] || value || "Mean";
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
    row.innerHTML = `
      <div>
        <strong>${user.userName}</strong>
        <span>${user.roles.join(", ")}</span>
      </div>
      <div class="row-actions">
        <button type="button" class="secondary compact-button user-edit-button">Edit</button>
        <button type="button" class="secondary compact-button danger-button user-delete-button">Delete</button>
      </div>`;
    row.querySelector(".user-edit-button").addEventListener("click", () => {
      $("setupUserName").value = user.userName;
      $("setupPassword").value = "";
      $("setupRole").value = user.roles[0] || state.roles[0] || "";
    });
    row.querySelector(".user-delete-button").addEventListener("click", () => deleteUser(user.userName));
    list.appendChild(row);
  });
}

async function deleteUser(userName) {
  try {
    await api(`/setup/users/${encodeURIComponent(userName)}`, { method: "DELETE" });
    $("userSetupMessage").textContent = `${userName} deleted.`;
    $("userSetupMessage").className = "message ok";
    await loadSetupAdmin();
  } catch (error) {
    $("userSetupMessage").textContent = readableError(error);
    $("userSetupMessage").className = "message error";
  }
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

function setupVariableRowTemplate() {
  return `
    <label class="setup-name-field"><span>Measurement</span><input class="setup-characteristic-name" required></label>
    <label class="setup-type-field">
      <span>Inspection type</span>
      <select class="setup-characteristic-type">
        <option value="Variable">Measured</option>
        <option value="Attribute">Accept / Reject</option>
      </select>
    </label>
    <label class="setup-unit-field"><span>Unit</span><input class="setup-unit" required></label>
    <label class="numeric-setup-field"><span>Target</span><input class="setup-nominal" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LSL</span><input class="setup-lsl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>USL</span><input class="setup-usl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LCL</span><input class="setup-lcl" type="number" step="0.0001"></label>
    <label class="numeric-setup-field"><span>UCL</span><input class="setup-ucl" type="number" step="0.0001"></label>
    <label class="setup-coa-field">
      <span>COA</span>
      <select class="setup-coa-required">
        <option value="true">Yes</option>
        <option value="false">No</option>
      </select>
    </label>
    <label class="setup-coa-stat-field">
      <span>COA stat</span>
      <select class="setup-coa-statistic">
        <option value="Mean">Mean</option>
        <option value="StandardDeviation">Std dev</option>
      </select>
    </label>
    <button type="button" class="secondary compact-button remove-variable-button">Remove</button>`;
}

function addSetupVariableRow(values = {}) {
  const row = document.createElement("div");
  row.className = "setup-variable-row";
  row.dataset.originalCharacteristicName = values.characteristicName || "";
  row.innerHTML = setupVariableRowTemplate();
  row.querySelector(".setup-characteristic-name").value = values.characteristicName || "";
  row.querySelector(".setup-characteristic-type").value = values.characteristicType || "Variable";
  row.querySelector(".setup-unit").value = values.unitOfMeasure || "";
  row.querySelector(".setup-nominal").value = values.nominal ?? "";
  row.querySelector(".setup-lsl").value = values.lsl ?? "";
  row.querySelector(".setup-usl").value = values.usl ?? "";
  row.querySelector(".setup-lcl").value = values.lcl ?? "";
  row.querySelector(".setup-ucl").value = values.ucl ?? "";
  row.querySelector(".setup-coa-required").value = String(values.isRequiredForCoa ?? true);
  row.querySelector(".setup-coa-statistic").value = values.coaStatisticType || "Mean";
  row.querySelector(".setup-characteristic-type").addEventListener("change", () => updateSetupVariableType(row));
  row.querySelector(".remove-variable-button").addEventListener("click", () => {
    if ($("setupVariableRows").children.length === 1) {
      row.querySelectorAll("input").forEach((input) => { input.value = ""; });
      row.querySelector(".setup-coa-required").value = "true";
      row.querySelector(".setup-coa-statistic").value = "Mean";
      return;
    }
    row.remove();
  });
  $("setupVariableRows").appendChild(row);
  updateSetupVariableType(row);
}

function updateSetupVariableType(row) {
  const isAttribute = row.querySelector(".setup-characteristic-type").value === "Attribute";
  const unit = row.querySelector(".setup-unit");
  const nominal = row.querySelector(".setup-nominal");
  const lsl = row.querySelector(".setup-lsl");
  const usl = row.querySelector(".setup-usl");
  const lcl = row.querySelector(".setup-lcl");
  const ucl = row.querySelector(".setup-ucl");
  row.classList.toggle("attribute-row", isAttribute);
  unit.required = !isAttribute;
  nominal.required = !isAttribute;
  lsl.required = !isAttribute;
  usl.required = !isAttribute;
  if (isAttribute) {
    unit.value = "Accept/Reject";
    nominal.value = "1";
    lsl.value = "0";
    usl.value = "1";
    lcl.value = "";
    ucl.value = "";
  }
}

function loadSelectedPartSetup() {
  const key = $("setupEditPartSelect").value;
  if (!key) {
    clearInspectionSetupForm();
    return;
  }

  const set = inspectionSets().find((item) => item.key === key);
  if (!set) {
    return;
  }

  $("setupPartNum").value = set.partNum;
  $("setupPartDescription").value = set.partDescription;
  $("setupProcessCode").value = set.processCode;
  $("setupProcessDescription").value = set.processDescription || set.processCode;
  $("setupOperationSeq").value = String(set.operationSeq || 10);
  state.editingSetup = {
    processCode: set.processCode,
    operationSeq: set.operationSeq || 10
  };
  const firstPlan = set.plans[0];
  $("setupInspectionPhase").value = firstPlan.inspectionPhase || "In Process";
  $("setupSampleSize").value = String(firstPlan.sampleSize || 1);
  $("setupFrequencyType").value = firstPlan.frequencyType;
  updateSetupFrequencyUnits();
  $("setupFrequencyValue").value = String(firstPlan.frequencyValue || 1);
  $("setupFrequencyUnit").value = firstPlan.frequencyUnit;
  $("setupAlertRuleSet").value = firstPlan.alertRuleSet || "WesternElectric";
  updateRuleDescription();
  $("setupVariableRows").innerHTML = "";
  set.plans.forEach((plan) => addSetupVariableRow(plan));
  $("inspectionSetupMessage").textContent = `${set.partNum} loaded for editing.`;
  $("inspectionSetupMessage").className = "message ok";
}

function clearInspectionSetupForm() {
  $("inspectionSetupForm").reset();
  $("setupEditPartSelect").value = "";
  state.editingSetup = null;
  $("setupOperationSeq").value = "10";
  $("setupProcessDescription").value = "";
  $("setupSampleSize").value = "5";
  $("setupFrequencyType").value = "Quantity";
  $("setupFrequencyValue").value = "10000";
  $("setupFrequencyUnit").value = "Pieces";
  $("setupAlertRuleSet").value = "GlobalDefault";
  $("setupInspectionPhase").value = "In Process";
  updateRuleDescription();
  updateSetupFrequencyUnits();
  $("setupVariableRows").innerHTML = "";
  addSetupVariableRow();
  $("inspectionSetupMessage").textContent = "";
  $("inspectionSetupMessage").className = "message";
}

function updateRuleDescription() {
  const globalRule = $("globalAlertRuleSet").value || state.snapshot?.settings?.globalAlertRuleSet || "WesternElectric";
  const descriptions = {
    GlobalDefault: `Uses the global default rule set: ${chartTypeLabel(globalRule)}. Change the global rule on the Rules tab, or override only this part when it needs different drift behavior.`,
    WesternElectric: "Applies four active checks: one point beyond a control limit, two of three points near a control limit, four of five points approaching a limit, or eight consecutive points on one side of center.",
    NelsonRules: "Includes the Western Electric checks and adds a six-point rising or falling trend signal.",
    Cusum: "Tracks cumulative deviation from center using one-half sigma as the reference value and five sigma as the action limit.",
    Ewma: "Uses an exponentially weighted moving average with lambda 0.20 and a three-sigma EWMA limit.",
    MovingAverageTrend: "Checks the latest five measurements and triggers when their average is at least one sigma from center.",
    LinearTrendSlope: "Checks the latest six measurements and triggers when the slope is strong enough and total movement is at least one sigma.",
    Custom: "Reserved for admin-defined behavior. For now, it triggers when the latest four points are beyond one sigma on the same side of center.",
    SpecLimitOnly: "Locks only when a value is outside the lower or upper specification limit.",
    None: "Records measured values without automatic drift locks. Accept/Reject failures still lock."
  };
  $("setupRuleDescription").textContent = descriptions[$("setupAlertRuleSet").value] || "";
}

async function saveGlobalRule(event) {
  event.preventDefault();
  try {
    const settings = await api("/setup/settings", {
      method: "POST",
      body: JSON.stringify({ globalAlertRuleSet: $("globalAlertRuleSet").value })
    });
    state.snapshot.settings = settings;
    $("globalRuleMessage").textContent = "Global rule saved.";
    $("globalRuleMessage").className = "message ok";
    updateRuleDescription();
  } catch (error) {
    $("globalRuleMessage").textContent = readableError(error);
    $("globalRuleMessage").className = "message error";
  }
}

function updateSetupFrequencyUnits() {
  const unitsByType = {
    Quantity: [["Pieces", "Pieces"]],
    Time: [["Minutes", "Minutes"], ["Hours", "Hours"]],
    Event: [["StartOfJob", "Start of job"], ["MaterialChange", "Material change"], ["ToolChange", "Tool change"], ["Restart", "Restart"]]
  };
  const current = $("setupFrequencyUnit").value;
  const units = unitsByType[$("setupFrequencyType").value] || unitsByType.Quantity;
  fillSelect($("setupFrequencyUnit"), units, (unit) => unit[0], (unit) => unit[1]);
  if (units.some((unit) => unit[0] === current)) {
    $("setupFrequencyUnit").value = current;
  }
}

function setupVariableRows() {
  return [...document.querySelectorAll(".setup-variable-row")].map((row) => ({
    originalCharacteristicName: row.dataset.originalCharacteristicName || null,
    characteristicName: row.querySelector(".setup-characteristic-name").value.trim(),
    characteristicType: row.querySelector(".setup-characteristic-type").value,
    unitOfMeasure: row.querySelector(".setup-unit").value.trim(),
    nominal: Number(row.querySelector(".setup-nominal").value),
    lsl: Number(row.querySelector(".setup-lsl").value),
    usl: Number(row.querySelector(".setup-usl").value),
    lcl: optionalInputNumber(row.querySelector(".setup-lcl")),
    ucl: optionalInputNumber(row.querySelector(".setup-ucl")),
    isRequiredForCoa: row.querySelector(".setup-coa-required").value === "true",
    coaStatisticType: row.querySelector(".setup-coa-statistic").value
  }));
}

function optionalInputNumber(input) {
  const value = input.value.trim();
  return value ? Number(value) : null;
}

async function saveInspectionSetup(event) {
  event.preventDefault();
  const variables = setupVariableRows();
  if (!variables.length || variables.some((variable) => !variable.characteristicName)) {
    $("inspectionSetupMessage").textContent = "Add at least one measurement name.";
    $("inspectionSetupMessage").className = "message error";
    return;
  }

  try {
    const baseRequest = {
      partNum: $("setupPartNum").value.trim(),
      partDescription: $("setupPartDescription").value.trim(),
      processCode: $("setupProcessCode").value.trim(),
      processDescription: $("setupProcessCode").value.trim(),
      operationSeq: Number($("setupOperationSeq").value),
      inspectionPhase: $("setupInspectionPhase").value,
      sampleSize: Number($("setupSampleSize").value),
      frequencyType: $("setupFrequencyType").value,
      frequencyValue: Number($("setupFrequencyValue").value),
      frequencyUnit: $("setupFrequencyUnit").value,
      alertRuleSet: $("setupAlertRuleSet").value
    };

    for (const variable of variables) {
      await api("/setup/inspection-plans", {
        method: "POST",
        body: JSON.stringify({
          ...baseRequest,
          characteristicName: variable.characteristicName,
          characteristicType: variable.characteristicType,
          nominal: variable.nominal,
          lsl: variable.lsl,
          usl: variable.usl,
          lcl: variable.lcl,
          ucl: variable.ucl,
          unitOfMeasure: variable.unitOfMeasure,
          isRequiredForCoa: variable.isRequiredForCoa,
          coaStatisticType: variable.coaStatisticType,
          originalProcessCode: state.editingSetup?.processCode || null,
          originalOperationSeq: state.editingSetup?.operationSeq || null,
          originalCharacteristicName: variable.originalCharacteristicName
        })
      });
    }

    $("inspectionSetupMessage").textContent = `${variables.length} variable${variables.length === 1 ? "" : "s"} saved for ${baseRequest.partNum}.`;
    $("inspectionSetupMessage").className = "message ok";
    state.editingSetup = {
      processCode: baseRequest.processCode,
      operationSeq: baseRequest.operationSeq
    };
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
    "PartNum,PartDescription,ProcessCode,ProcessDescription,OperationSeq,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,InspectionPhase,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA,COAStatistic",
    "P200,Example part,MOLD,Molding,10,Measurement 1,Variable,5.0,4.5,5.5,4.4,5.6,mm,Startup,5,Event,1,StartOfJob,WesternElectric,true,Mean",
    "P200,Example part,MOLD,Molding,10,Measurement 2,Variable,42.0,41.5,42.5,41.0,43.0,mm,In Process,5,Quantity,10000,Pieces,NelsonRules,true,StandardDeviation"
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

function formatDateTime(value) {
  return new Date(value).toLocaleString([], {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  });
}

function ruleLabel(rule) {
  return {
    OnePointBeyondControlLimit: "One point beyond control limit",
    TwoOfThreeNearControlLimit: "Two of three near control limit",
    FourOfFiveApproachingLimit: "Four of five approaching limit",
    EightConsecutiveOneSideOfCenterline: "Eight consecutive one side of centerline",
    SpecLimitViolation: "Spec limit violation",
    NelsonTrend: "Nelson trend",
    CusumShift: "CUSUM shift",
    EwmaShift: "EWMA shift",
    MovingAverageTrend: "Moving average trend",
    LinearTrendSlope: "Linear trend / slope",
    CustomRuleTriggered: "Custom rule triggered",
    AttributeRejected: "Attribute rejected"
  }[rule] || rule;
}

window.addEventListener("online", () => setStatus($("syncStatus"), "Online", "ok"));
window.addEventListener("offline", () => setStatus($("syncStatus"), "Offline", "warn"));
$("loginForm").addEventListener("submit", login);
$("contextForm").addEventListener("submit", loadContext);
$("jobNum").addEventListener("input", clearWorkContext);
$("partNum").addEventListener("input", clearWorkContext);
$("resourceId").addEventListener("change", clearWorkContext);
$("logoutButton").addEventListener("click", logout);
$("measurementForm").addEventListener("submit", submitMeasurement);
$("materialForm").addEventListener("submit", saveMaterialChange);
$("jobNoteForm").addEventListener("submit", saveJobNote);
$("overrideForm").addEventListener("submit", clearLock);
$("overrideUserName").addEventListener("input", () => {
  $("godReasonLabel").classList.toggle("hidden", $("overrideUserName").value.trim().toLowerCase() !== "god1");
});
$("trendCharacteristic").addEventListener("change", () => {
  state.trendCharacteristic = $("trendCharacteristic").value;
  loadTrend();
});
$("trendChartType").addEventListener("change", () => {
  state.trendChartType = $("trendChartType").value;
  loadTrend();
});
$("inspectionTab").addEventListener("click", () => showPanel("inspect"));
$("setupTab").addEventListener("click", () => showPanel("setup"));
$("setupInspectionSectionTab").addEventListener("click", () => showSetupSection("Inspection"));
$("setupUsersSectionTab").addEventListener("click", () => showSetupSection("Users"));
$("setupRulesSectionTab").addEventListener("click", () => showSetupSection("Rules"));
$("setupImportSectionTab").addEventListener("click", () => showSetupSection("Import"));
$("setupReviewSectionTab").addEventListener("click", () => showSetupSection("Review"));
$("setupJobDataSectionTab").addEventListener("click", () => showSetupSection("JobData"));
$("userSetupForm").addEventListener("submit", saveUser);
$("inspectionSetupForm").addEventListener("submit", saveInspectionSetup);
$("addSetupVariableButton").addEventListener("click", () => addSetupVariableRow());
$("clearInspectionSetupButton").addEventListener("click", clearInspectionSetupForm);
$("loadPartSetupButton").addEventListener("click", loadSelectedPartSetup);
$("setupFrequencyType").addEventListener("change", updateSetupFrequencyUnits);
$("setupAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalRuleForm").addEventListener("submit", saveGlobalRule);
$("csvImportForm").addEventListener("submit", importCsv);
$("csvTemplateButton").addEventListener("click", loadCsvTemplate);
$("partReviewFilter").addEventListener("change", loadReview);
$("reviewLoadButton").addEventListener("click", loadReview);
$("reviewJobNum").addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    loadReview();
  }
});
$("jobSummaryForm").addEventListener("submit", loadJobSummary);
$("jobSummaryCsvButton").addEventListener("click", openJobSummaryCsv);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");
clearInspectionSetupForm();

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}

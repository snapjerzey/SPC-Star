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
        productGroup: plan.productGroup || "General",
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
  fillDatalist($("productGroupOptions"), productGroups(), (group) => group);
  $("partNum").value = "";
  if (canManageSetup()) {
    renderGlobalRuleSetting();
    renderPartReviewControls();
    renderReportControls();
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

function productGroups() {
  return [...new Set((state.snapshot?.parts || []).map((part) => part.productGroup || "General"))].sort();
}

function normalizeInspectionPhase(value) {
  if (!value) return "In Process";
  const phase = value.trim().toLowerCase();
  if (phase === "startup") return "Startup";
  if (phase === "set up" || phase === "setup") return "Setup";
  if (phase === "spool" || phase === "spool start" || phase === "spool end") return "Spool";
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
  $("contextTitle").textContent = "Variables";
  $("contextSubtitle").textContent = "Enter a job number, machine, and part number, then start inspecting.";
  renderLock(null);
  $("measurementForm").classList.add("hidden");
  $("trendPanel").classList.add("hidden");
  $("jobNotesPanel").classList.add("hidden");
  $("tagsDivider").classList.add("hidden");
  $("tagsSection").classList.add("hidden");
  $("measurementVariableList").innerHTML = "";
  $("attributeVariableList").innerHTML = "";
  $("meanSummary").innerHTML = "";
  $("trendCharacteristic").innerHTML = "";
  $("entryMessage").textContent = message;
  $("entryMessage").className = message ? "message error" : "message";
  $("jobTagsForm").innerHTML = "";
  $("jobTagsForm").classList.add("hidden");
  $("tagMessage").textContent = "";
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
  $("contextTitle").textContent = "Variables";
  $("contextSubtitle").textContent = `${jobNum} / ${resourceId} / ${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.inspectionPhase}`;
  $("measurementForm").classList.remove("hidden");
  $("trendPanel").classList.remove("hidden");
  $("jobNotesPanel").classList.remove("hidden");
  renderConfiguredJobDataFields(set);
  const hasConfiguredTags = document.querySelectorAll(".job-tag-input").length > 0;
  $("tagsDivider").classList.toggle("hidden", !hasConfiguredTags);
  $("tagsSection").classList.toggle("hidden", !hasConfiguredTags);
  $("jobTagsForm").classList.toggle("hidden", !hasConfiguredTags);
  state.activeLock = state.contexts.find((context) => context.activeLock)?.activeLock || null;
  renderLock(state.activeLock);
  renderVariables();
  renderMeanSummary();
  renderTrendChoices();
  loadTrend();
  loadJobNotes(jobNum);
  loadJobTags(jobNum);
}

function renderConfiguredJobDataFields(set) {
  const fields = (state.snapshot.partJobDataFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(set.inspectionPhase))
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
  const form = $("jobTagsForm");
  form.innerHTML = fields.map((field) => `
    <label>
      ${escapeHtml(field.fieldName)}
      <input class="job-tag-input" data-tag-name="${escapeHtml(field.fieldName)}" autocomplete="off" ${field.isRequired ? "required" : ""}>
    </label>`).join("") + (fields.length ? `<button type="submit" class="secondary">Save Job Data</button>` : "");
}

function renderLock(activeLock) {
  const banner = $("lockBanner");
  const panel = $("overridePanel");
  if (!activeLock) {
    banner.classList.add("hidden");
    banner.textContent = "";
    panel.classList.add("hidden");
    document.body.classList.remove("lock-active");
    $("overrideMessage").textContent = "";
    return;
  }
  banner.classList.remove("hidden");
  const lockText = `LOCKED: ${activeLock.characteristicName} - ${ruleLabel(activeLock.ruleTriggered)} at ${formatTime(activeLock.lockedAt)}${activeLock.detail ? `. ${activeLock.detail}` : ""}`;
  banner.textContent = lockText;
  panel.classList.remove("hidden");
  document.body.classList.add("lock-active");
  panel.querySelector(".panel-heading p")?.remove();
  const detail = document.createElement("p");
  detail.textContent = lockText;
  panel.querySelector(".panel-heading").appendChild(detail);
  $("overrideUserName").value = canCurrentUserOverride() ? state.user.userName : "";
  $("godReasonLabel").classList.toggle("hidden", !state.user?.roles?.includes("GOD"));
}

function renderVariables() {
  const measurementList = $("measurementVariableList");
  const attributeList = $("attributeVariableList");
  measurementList.innerHTML = "";
  attributeList.innerHTML = "";
  measurementList.appendChild(sectionHeading("Variables"));
  attributeList.appendChild(sectionHeading("Attributes"));
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
              <input class="measurement-input" data-plan-index="${index}" data-sample-index="${sampleIndex}" data-entry-type="Variable" type="text" inputmode="decimal" autocomplete="off" placeholder="0.0000">`}
          </label>`).join("")}
      </div>`;
    (isAttribute ? attributeList : measurementList).appendChild(card);
  });
  if (!attributeList.querySelector(".variable-card")) {
    attributeList.classList.add("hidden");
  } else {
    attributeList.classList.remove("hidden");
  }
  wireMeasurementDeviceInputs();
}

function sectionHeading(text) {
  const heading = document.createElement("h3");
  heading.className = "inspection-section-heading";
  heading.textContent = text;
  return heading;
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
      <span>Min</span>
      <span>Max</span>
      <span>Mean</span>
      <span>Std Dev</span>
      <span>Cp</span>
      <span>Cpk</span>
      <span>Pp</span>
      <span>Ppk</span>
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
        <span class="muted-cell">-</span>
        <span class="muted-cell">-</span>
        <span>${accepted}/${points.length || 0}</span>
        <span class="muted-cell">Accept/Reject</span>
        <span class="muted-cell">-</span>
        <span class="muted-cell">-</span>
        <span class="muted-cell">-</span>`;
      summary.appendChild(item);
      return;
    }
    const values = points.map((point) => Number(point.value)).filter(Number.isFinite);
    const capability = state.contexts[index]?.capability || {};
    item.innerHTML = `
      <span>${plan.characteristicName}</span>
      <span>${formatNumber(values.length ? Math.min(...values) : null)}</span>
      <span>${formatNumber(values.length ? Math.max(...values) : null)}</span>
      <span>${formatNumber(mean)}</span>
      <span>${formatNumber(standardDeviation(values))}</span>
      <span>${capabilityBadge(capability.cp)}</span>
      <span>${capabilityBadge(capability.cpk)}</span>
      <span>${capabilityBadge(capability.pp)}</span>
      <span>${capabilityBadge(capability.ppk)}</span>`;
    summary.appendChild(item);
  });
}

function capabilityBadge(value) {
  return `<span class="capability-chip ${capabilityClass(value)}">${formatNumber(value)}</span>`;
}

function standardDeviation(values) {
  if (values.length < 2) return null;
  const mean = values.reduce((total, value) => total + value, 0) / values.length;
  const variance = values.reduce((total, value) => total + ((value - mean) ** 2), 0) / (values.length - 1);
  return Math.sqrt(variance);
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
  const activeInput = document.activeElement?.classList?.contains("measurement-input")
    ? document.activeElement
    : null;
  if (activeInput) {
    await submitSingleMeasurementAndAdvance(activeInput, false);
    return;
  }
  showEntryMessage("Measurements submit automatically when you leave each field.", "ok");
}

function inputHasValue(input) {
  return input.value.trim().length > 0;
}

async function submitSingleMeasurementAndAdvance(input, moveNext, options = {}) {
  normalizeMeasurementInput(input);
  if (!inputHasValue(input)) {
    showEntryMessage(`Fill in ${sampleLabel(input)} before moving on.`, "error");
    if (options.keepFocusWhenEmpty) {
      input.focus();
    }
    return "empty";
  }

  try {
    const result = await submitMeasurementInput(input, { reloadOnSuccess: false });
    if (moveNext && result !== "locked" && !state.activeLock) {
      focusNextMeasurementInput(input);
    }
  } catch {
    input.focus();
    input.select?.();
  }
}

function wireMeasurementDeviceInputs() {
  document.querySelectorAll(".measurement-input").forEach((input) => {
    input.addEventListener("focus", () => input.closest("label")?.classList.add("device-input-active"));
    input.addEventListener("blur", async () => {
      input.closest("label")?.classList.remove("device-input-active");
      if (input.dataset.tabSubmitting === "true" || input.disabled || input.dataset.submitted === "true") {
        return;
      }
      await submitSingleMeasurementAndAdvance(input, false);
    });
    input.addEventListener("change", async () => {
      if (input.dataset.entryType === "Attribute" && inputHasValue(input)) {
        await submitSingleMeasurementAndAdvance(input, false);
      }
    });
    input.addEventListener("keydown", async (event) => {
      if (event.key !== "Enter" && event.key !== "Tab") return;
      if (event.key === "Tab" && !inputHasValue(input)) {
        showEntryMessage(`Fill in ${sampleLabel(input)} before moving on.`, "error");
        return;
      }
      event.preventDefault();
      input.dataset.tabSubmitting = "true";
      await submitSingleMeasurementAndAdvance(input, true, { keepFocusWhenEmpty: event.key === "Enter" });
      input.dataset.tabSubmitting = "false";
    });
    input.addEventListener("paste", () => {
      window.setTimeout(() => normalizeMeasurementInput(input), 0);
    });
  });
}

async function submitMeasurementInput(input, options = {}) {
  if (input.disabled || input.dataset.submitting === "true" || input.dataset.submitted === "true") return;
  if (!inputHasValue(input)) return;
  const { jobNum, resourceId } = selectedValues();
  const plan = state.selectedPlans[Number(input.dataset.planIndex)];
  const value = Number(input.value);
  if (!Number.isFinite(value)) {
    showEntryMessage(`${sampleLabel(input)} must be numeric.`, "error");
    throw new Error(`${sampleLabel(input)} must be numeric.`);
  }

  input.dataset.submitting = "true";
  try {
    await api("/inspections/measurements", {
      method: "POST",
      body: JSON.stringify({
        jobNum,
        partNum: plan.partNum,
        processCode: plan.processCode,
        operationSeq: plan.operationSeq,
        resourceId,
        characteristicName: plan.characteristicName,
        inspectionPhase: plan.inspectionPhase || $("inspectionPhase").value,
        value,
        timestamp: new Date().toISOString(),
        operatorUserId: state.user.userName,
        deviceId: "browser-dev",
        clientRecordId: newClientRecordId(),
        submittedAt: new Date().toISOString()
      })
    });
    showEntryMessage(`${sampleLabel(input)} submitted.`, "ok");
    const planIndex = Number(input.dataset.planIndex);
    state.contexts[planIndex] = await loadVariableContext(jobNum, resourceId, plan);
    renderMeanSummary();
    if (state.contexts[planIndex]?.activeLock) {
      state.activeLock = state.contexts[planIndex].activeLock;
      await loadContext();
      return "locked";
    }
    resetAcceptedMeasurementInput(input);
    if (options.reloadOnSuccess === true) {
      await loadContext();
    }
    return "submitted";
  } catch (error) {
    showEntryMessage("Measurement rejected. " + readableError(error), "error");
    await loadContext();
    throw error;
  } finally {
    input.dataset.submitting = "false";
  }
}

function resetAcceptedMeasurementInput(input) {
  delete input.dataset.submitted;
  input.value = "";
  input.disabled = false;
  const label = input.closest("label");
  label?.classList.add("measurement-submitted");
  window.setTimeout(() => label?.classList.remove("measurement-submitted"), 900);
}

function sampleLabel(input) {
  const plan = state.selectedPlans[Number(input.dataset.planIndex)];
  return `${plan?.characteristicName || "value"} sample ${Number(input.dataset.sampleIndex) + 1}`;
}

function normalizeMeasurementInput(input) {
  if (input.dataset.entryType !== "Variable") return;
  const parsed = parseDeviceMeasurement(input.value);
  if (parsed !== null) {
    input.value = parsed;
  }
}

function parseDeviceMeasurement(rawValue) {
  const match = String(rawValue).replace(",", ".").match(/[-+]?\d*\.?\d+/);
  return match ? match[0] : null;
}

function focusNextMeasurementInput(currentInput) {
  const inputs = [...document.querySelectorAll(".measurement-input")];
  const index = inputs.indexOf(currentInput);
  const next = inputs.slice(index + 1).find((input) => !input.disabled);
  if (next) {
    next.focus();
    next.select?.();
  }
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

const RULE_DETAILS = {
  GlobalDefault: {
    title: "Use Global Default",
    subtitle: "Part-level inheritance",
    body: `
      <p>This option tells a part setup to use whatever rule is selected as the plant-wide global default. It is useful when most parts should follow the same drift detection protocol.</p>
      <h3>How it works</h3>
      <ul>
        <li>The global rule is selected at the top of the Rules tab.</li>
        <li>Any part set to Use Global Default follows that global rule automatically.</li>
        <li>If the global rule is changed later, those inherited part setups change with it.</li>
        <li>Parts with a specific override keep their own rule and do not follow the global change.</li>
      </ul>`
  },
  WesternElectric: {
    title: "Western Electric",
    subtitle: "Classic control-chart pattern detection",
    body: `
      <p>Western Electric rules look for non-random process behavior on a control chart. They are designed to catch process shifts before the process produces a long run of bad parts.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>One point beyond a control limit triggers a lock.</li>
        <li>Two of three recent points near the same control limit triggers a lock.</li>
        <li>Four of five recent points moving toward the same limit triggers a lock.</li>
        <li>Eight consecutive points on the same side of centerline triggers a lock.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use this as the standard process-control method for stable, measured variables where control limits are meaningful and enough history exists to detect patterns.</p>`
  },
  NelsonRules: {
    title: "Nelson Rules",
    subtitle: "Extended trend and pattern detection",
    body: `
      <p>Nelson rules expand on control-chart logic by adding more pattern checks. SPC-Star currently applies the Western Electric checks and adds a Nelson-style six-point trend rule.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>All active Western Electric checks are still evaluated.</li>
        <li>Six consecutive points rising triggers a Nelson trend lock.</li>
        <li>Six consecutive points falling triggers a Nelson trend lock.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use Nelson Rules when gradual tool wear, material change, machine warm-up, or operator method changes can create a steady directional drift before values exceed limits.</p>`
  },
  Cusum: {
    title: "CUSUM",
    subtitle: "Cumulative sum shift detection",
    body: `
      <p>CUSUM tracks accumulated deviation from the process center. Small offsets that look harmless one point at a time can become obvious when their cumulative effect keeps building.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>The process centerline is used as the target.</li>
        <li>The reference value is one-half sigma.</li>
        <li>The action limit is five sigma.</li>
        <li>If the positive or negative cumulative sum exceeds the action limit, SPC-Star triggers a CUSUM shift lock.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use CUSUM when you care about small sustained shifts, such as tool wear or a machine slowly moving away from target.</p>`
  },
  Ewma: {
    title: "EWMA",
    subtitle: "Exponentially weighted moving average",
    body: `
      <p>EWMA smooths the process using a weighted average. Recent measurements matter most, but older measurements still influence the signal.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>Lambda is 0.20, so recent points receive 20 percent of the new weighted average.</li>
        <li>The starting EWMA value is the process centerline.</li>
        <li>The EWMA limit is calculated from sigma and lambda.</li>
        <li>If the weighted average moves beyond the EWMA limit, SPC-Star triggers an EWMA shift lock.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use EWMA for noisy measurements where individual points bounce around but the smoothed process direction matters.</p>`
  },
  MovingAverageTrend: {
    title: "Moving Average Trend",
    subtitle: "Recent-window average shift",
    body: `
      <p>Moving average trend detection compares the average of a recent group of points against the centerline. It is simpler than EWMA and easy to explain on the floor.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>The latest five measurements are averaged.</li>
        <li>If that average is at least one sigma away from center, SPC-Star triggers a moving-average trend lock.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use this when short-term process movement matters and you want a clear recent-sample rule.</p>`
  },
  LinearTrendSlope: {
    title: "Linear Trend / Slope",
    subtitle: "Directional drift over a recent window",
    body: `
      <p>Linear trend detection fits a simple slope across recent points. It looks for steady movement in one direction, not just values sitting above or below center.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>The latest six measurements are evaluated.</li>
        <li>SPC-Star calculates the slope across the window.</li>
        <li>The slope must be strong enough and the total movement across the window must be at least one sigma.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use this for gradual wear patterns where each value may still be acceptable, but the direction is clearly heading toward trouble.</p>`
  },
  Custom: {
    title: "Custom Rule",
    subtitle: "Plant-defined drift protocol",
    body: `
      <p>Custom lets SPC-Star use a plant-defined rule instead of one fixed statistical method. Configure the recent point window, sigma threshold, direction, and how many points must cross that threshold.</p>
      <h3>Current engine behavior</h3>
      <ul>
        <li>SPC-Star looks at the most recent configured number of measurements.</li>
        <li>It compares those values to centerline plus or minus the configured sigma threshold.</li>
        <li>It triggers when the configured number of points exceed that threshold in the selected direction.</li>
        <li>The option to include Western Electric checks keeps the standard control-chart rules active alongside the custom rule.</li>
      </ul>`
  },
  SpecLimitOnly: {
    title: "Spec Limit Only",
    subtitle: "Customer specification guardrail",
    body: `
      <p>Spec Limit Only ignores drift patterns and locks only when a value is outside the lower or upper specification limit.</p>
      <h3>Protocol used in SPC-Star</h3>
      <ul>
        <li>A value below LSL locks the inspection.</li>
        <li>A value above USL locks the inspection.</li>
        <li>Control-limit drift patterns do not create locks.</li>
      </ul>
      <h3>When to use it</h3>
      <p>Use this when only direct pass/fail conformance should stop the process, or when control limits are not yet mature.</p>`
  },
  None: {
    title: "No Automatic Rule",
    subtitle: "Record only",
    body: `
      <p>This setting records measurements without automatic drift or measured-variable lockouts. It should be used carefully because SPC-Star will not stop the operator for measured-variable drift.</p>
      <h3>What still applies</h3>
      <ul>
        <li>Inspection data is still saved.</li>
        <li>Charts and review data still update.</li>
        <li>Accept/Reject attribute failures still lock because they are direct failed inspections.</li>
      </ul>`
  }
};

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

async function loadJobTags(jobNum) {
  document.querySelectorAll(".job-tag-input").forEach((input) => {
    input.value = "";
  });

  if (!jobNum) return;

  try {
    const tags = await api(`/jobs/${encodeURIComponent(jobNum)}/tags`);
    tags.forEach((tag) => {
      const input = [...document.querySelectorAll(".job-tag-input")]
        .find((field) => field.dataset.tagName.toLowerCase() === tag.tagName.toLowerCase());
      if (input) {
        input.value = tag.tagValue || "";
      }
    });
    $("tagMessage").textContent = tags.length ? `Loaded ${tags.length} job tag${tags.length === 1 ? "" : "s"}.` : "";
    $("tagMessage").className = "message";
  } catch (error) {
    $("tagMessage").textContent = readableError(error);
    $("tagMessage").className = "message error";
  }
}

async function saveJobTags(event) {
  event.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  if (!jobNum || !resourceId || !set) {
    $("tagMessage").textContent = "Start work before saving tags.";
    $("tagMessage").className = "message error";
    return;
  }

  const tags = {};
  document.querySelectorAll(".job-tag-input").forEach((input) => {
    tags[input.dataset.tagName] = input.value.trim();
  });

  try {
    await api(`/jobs/${encodeURIComponent(jobNum)}/tags`, {
      method: "POST",
      body: JSON.stringify({
        jobNum,
        partNum: set.partNum,
        resourceId,
        operatorUserId: state.user.userName,
        tags,
        updatedAt: new Date().toISOString()
      })
    });
    $("tagMessage").textContent = "Tags saved.";
    $("tagMessage").className = "message ok";
    await loadJobNotes(jobNum);
  } catch (error) {
    $("tagMessage").textContent = readableError(error);
    $("tagMessage").className = "message error";
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
    } else if (entry.entryType === "MeasurementEdit") {
      text.textContent = measurementEditHistoryText(entry);
    } else if (entry.entryType === "Measurement") {
      text.textContent = measurementHistoryText(entry);
    } else {
      text.textContent = entry.noteText;
    }
    meta.append(user, details);
    item.append(meta, text);
    list.appendChild(item);
  });
}

function historyEntryTitle(entry) {
  if (entry.entryType === "Measurement") {
    return `${entry.characteristicName} inspection`;
  }

  if (entry.entryType === "MeasurementEdit") {
    return `${entry.characteristicName} edited`;
  }

  if (entry.entryType === "Lock") {
    return `${entry.characteristicName} ${entry.status === "Active" ? "locked" : "lock cleared"}`;
  }

  if (entry.entryType === "Material") {
    return `Material ${entry.reason || "event"}`;
  }

  return entry.operatorUserId;
}

function historyEntryUser(entry) {
  if (entry.entryType === "Lock" && entry.status !== "Active" && entry.overrideUserId) {
    return entry.overrideUserId;
  }

  return entry.operatorUserId || "-";
}

function measurementHistoryText(entry) {
  const value = entry.characteristicType === "Attribute"
    ? Number(entry.value) === 1 ? "Accept" : "Reject"
    : formatNumber(entry.value);
  const flags = entry.isOutOfSpec ? " Out of spec." : entry.isOutOfControl ? " Out of control." : "";
  return `${entry.inspectionPhase}: ${value} on ${entry.resourceId}.${flags}`;
}

function measurementEditHistoryText(entry) {
  return `Edited from ${entry.oldInspectionPhase}: ${formatNumber(entry.oldValue)} to ${entry.newInspectionPhase}: ${formatNumber(entry.newValue)} by ${entry.operatorUserId}.`;
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
  const sections = ["Inspection", "Users", "Rules", "Import", "Review", "Reports", "JobData"];
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

function renderReportControls() {
  fillSelect($("reportResourceId"), [{ resourceId: "", description: "All machines" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillDatalist($("reportCharacteristicOptions"), state.snapshot.characteristics, (characteristic) => characteristic.name);
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
    label: `${set.partNum} / ${set.productGroup || "General"} / ${set.processCode} / ${set.inspectionPhase}`
  }))];
  fillSelect($("setupEditPartSelect"), sets, (set) => set.key, (set) => set.label);
}

function renderGlobalRuleSetting() {
  $("globalAlertRuleSet").value = state.snapshot.settings?.globalAlertRuleSet || "WesternElectric";
  updateRuleDescription();
  loadCustomRuleForm();
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
  renderReviewMeasurements(review.measurements || [], review.history || []);
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
      body: JSON.stringify({ value, inspectionPhase, editedByUserId: state.user.userName })
    });
    $("reviewMessage").textContent = "Inspection entry updated.";
    $("reviewMessage").className = "message ok";
    await loadReview();
  } catch (error) {
    $("reviewMessage").textContent = readableError(error);
    $("reviewMessage").className = "message error";
  }
}

function renderReviewMeasurements(measurements, history) {
  const container = $("jobReviewMeasurements");
  const rows = [
    ...measurements.map((measurement) => ({ kind: "Measurement", timestamp: measurement.timestamp, measurement })),
    ...history.map((entry) => ({ kind: "History", timestamp: entry.timestamp, entry }))
  ].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));

  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = "No job history for this job.";
    return;
  }

  container.className = "data-table review-measurement-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Time</span><span>Phase</span><span>Type</span><span>Value / Details</span><span>Machine</span><span>Operation</span><span>User</span><span></span>
    </div>`;
  rows.forEach((row) => {
    if (row.kind === "History") {
      renderReviewHistoryEvent(container, row.entry);
      return;
    }

    const measurement = row.measurement;
    const item = document.createElement("div");
    item.className = `data-row ${measurement.isOutOfSpec ? "measurement-out-spec" : measurement.isOutOfControl ? "measurement-out-control" : ""}`;
    item.innerHTML = `
      <span>${formatDateTime(measurement.timestamp)}</span>
      <span>
        <select class="review-measurement-phase">
          <option value="Startup" ${measurement.inspectionPhase === "Startup" ? "selected" : ""}>Startup</option>
          <option value="Setup" ${measurement.inspectionPhase === "Setup" ? "selected" : ""}>Setup</option>
          <option value="In Process" ${measurement.inspectionPhase === "In Process" ? "selected" : ""}>In Process</option>
          <option value="Spool" ${normalizeInspectionPhase(measurement.inspectionPhase) === "Spool" ? "selected" : ""}>Spool</option>
        </select>
      </span>
      <span>${measurement.characteristicName}</span>
      <span>${reviewMeasurementValueControl(measurement)}</span>
      <span>${measurement.resourceId}${measurement.isOutOfSpec ? ` <strong class="status-text bad">Out of spec</strong>` : measurement.isOutOfControl ? ` <strong class="status-text warn">Out of control</strong>` : ""}</span>
      <span>${measurement.processCode} ${measurement.operationSeq}</span>
      <span>${measurement.operatorUserId}</span>
      <span><button type="button" class="secondary compact-button">Save</button></span>`;
    item.querySelector("button").addEventListener("click", () => saveReviewMeasurement(measurement.id, item));
    container.appendChild(item);
  });
}

function renderReviewHistoryEvent(container, entry) {
  const item = document.createElement("div");
  item.className = `data-row review-history-event-row ${entry.entryType === "Lock" ? "measurement-out-control" : ""} ${entry.entryType === "MeasurementEdit" ? "measurement-edit-history" : ""}`;
  const details = entry.entryType === "Lock"
    ? lockHistoryText(entry)
    : entry.entryType === "Material"
      ? materialHistoryText(entry)
      : entry.entryType === "MeasurementEdit"
        ? measurementEditHistoryText(entry)
        : entry.noteText;
  item.innerHTML = `
    <span>${formatDateTime(entry.timestamp)}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryTitle(entry))}</span>
    <span>${escapeHtml(details || "")}</span>
    <span>${escapeHtml(entry.resourceId || "-")}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryUser(entry))}</span>
    <span></span>`;
  container.appendChild(item);
}

function reviewMeasurementValueControl(row) {
  if (row.characteristicType === "Attribute") {
    return `
      <select class="review-measurement-value">
        <option value="1" ${Number(row.value) === 1 ? "selected" : ""}>Accept</option>
        <option value="0" ${Number(row.value) === 0 ? "selected" : ""}>Reject</option>
      </select>`;
  }

  return `<input class="review-measurement-value" type="number" step="0.0001" value="${Number(row.value)}">`;
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
      <span>Job</span><span>Variable</span><span>COA Stat</span><span>COA Value</span><span>Mean</span><span>Min</span><span>Max</span><span>Std Dev</span><span>Cpk</span><span>Ppk</span>
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
      <span>${formatNumber(row.min)}</span>
      <span>${formatNumber(row.max)}</span>
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

async function runReport(event) {
  event.preventDefault();
  try {
    const data = await api("/charts/data", {
      method: "POST",
      body: JSON.stringify({
        chartType: "IndividualsMovingRange",
        jobNum: $("reportJobNum").value.trim() || null,
        partNum: $("reportPartNum").value.trim() || null,
        resourceId: $("reportResourceId").value || null,
        characteristicName: $("reportCharacteristicName").value.trim() || null,
        from: dateTimeLocalValue("reportFrom"),
        to: dateTimeLocalValue("reportTo"),
        inspectionPhase: $("reportInspectionPhase").value || null
      })
    });
    drawReport(data.points, data);
    $("reportMessage").textContent = `${data.points.length} point${data.points.length === 1 ? "" : "s"} loaded.`;
    $("reportMessage").className = "message ok";
  } catch (error) {
    $("reportMessage").textContent = readableError(error);
    $("reportMessage").className = "message error";
  }
}

function dateTimeLocalValue(id) {
  const value = $(id).value;
  return value ? new Date(value).toISOString() : null;
}

function drawReport(points, data = {}) {
  const canvas = $("reportCanvas");
  const ctx = canvas.getContext("2d");
  const width = canvas.width;
  const height = canvas.height;
  ctx.clearRect(0, 0, width, height);
  ctx.fillStyle = "#ffffff";
  ctx.fillRect(0, 0, width, height);
  const padding = { left: 62, right: 78, top: 58, bottom: 48 };
  const plotWidth = width - padding.left - padding.right;
  const plotHeight = height - padding.top - padding.bottom;
  const chartType = $("reportChartType").value;
  drawReportHeader(ctx, width, points, data, chartType);
  drawChartFrame(ctx, padding, plotWidth, plotHeight);
  if (!points.length) {
    drawEmptyChartMessage(ctx, width, height);
    return;
  }

  const values = points.map((point) => Number(point.value));
  const limitValues = [data.lowerControlLimit, data.upperControlLimit, data.lowerSpecLimit, data.upperSpecLimit]
    .filter((value) => value !== null && value !== undefined)
    .map(Number);
  const min = Math.min(...values, ...limitValues);
  const max = Math.max(...values, ...limitValues);
  const spread = max === min ? 1 : max - min;
  const low = min - spread * 0.1;
  const high = max + spread * 0.1;

  if (chartType === "Histogram") {
    drawHistogram(ctx, points, padding, plotWidth, plotHeight, low, high);
    drawHistogramDetails(ctx, points, padding, plotWidth, plotHeight, low, high);
    return;
  }

  if (chartType === "MovingRange") {
    drawMovingRange(ctx, points, padding, plotWidth, plotHeight);
    drawMovingRangeDetails(ctx, points, padding, plotWidth, plotHeight);
    drawXAxisDetails(ctx, points, padding, plotWidth, plotHeight);
    return;
  }

  const x = (index) => padding.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
  const y = (value) => padding.top + (1 - ((Number(value) - low) / (high - low))) * plotHeight;
  drawYAxisDetails(ctx, padding, plotWidth, plotHeight, low, high);
  drawXAxisDetails(ctx, points, padding, plotWidth, plotHeight);
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
  drawPointValueDetails(ctx, points, (point, index) => x(index), (point) => y(point.value));
}

function drawReportHeader(ctx, width, points, data, chartType) {
  const values = points.map((point) => Number(point.value)).filter(Number.isFinite);
  const stats = values.length ? {
    count: values.length,
    min: Math.min(...values),
    max: Math.max(...values),
    mean: values.reduce((total, value) => total + value, 0) / values.length,
    stdDev: standardDeviation(values)
  } : null;
  const filters = [
    $("reportPartNum").value.trim() || "All parts",
    $("reportJobNum").value.trim() || "All jobs",
    $("reportCharacteristicName").value.trim() || "All variables",
    $("reportInspectionPhase").value || "All phases"
  ].join(" / ");
  ctx.fillStyle = "#0f172a";
  ctx.font = "700 14px Segoe UI, Arial";
  ctx.fillText(`${chartTypeLabel(chartType)} Report`, 12, 20);
  ctx.fillStyle = "#5f6f82";
  ctx.font = "11px Segoe UI, Arial";
  ctx.fillText(filters, 12, 38);
  if (!stats) return;
  const summary = `Count ${stats.count}   Min ${formatNumber(stats.min)}   Max ${formatNumber(stats.max)}   Mean ${formatNumber(data.mean ?? stats.mean)}   Std Dev ${formatNumber(stats.stdDev)}`;
  ctx.fillStyle = "#344054";
  ctx.textAlign = "right";
  ctx.fillText(summary, width - 12, 20);
  ctx.textAlign = "left";
}

function drawEmptyChartMessage(ctx, width, height) {
  ctx.fillStyle = "#5f6f82";
  ctx.font = "13px Segoe UI, Arial";
  ctx.textAlign = "center";
  ctx.fillText("No matching data for this report.", width / 2, height / 2);
  ctx.textAlign = "left";
}

function drawYAxisDetails(ctx, padding, plotWidth, plotHeight, low, high) {
  ctx.fillStyle = "#5f6f82";
  ctx.font = "11px Segoe UI, Arial";
  ctx.textAlign = "right";
  for (let index = 0; index <= 4; index++) {
    const value = high - ((high - low) / 4) * index;
    const y = padding.top + (plotHeight / 4) * index;
    ctx.fillText(formatNumber(value), padding.left - 8, y + 4);
  }
  ctx.textAlign = "left";
}

function drawXAxisDetails(ctx, points, padding, plotWidth, plotHeight) {
  if (!points.length) return;
  const indexes = [...new Set([0, Math.floor((points.length - 1) / 2), points.length - 1])];
  ctx.fillStyle = "#5f6f82";
  ctx.font = "10px Segoe UI, Arial";
  ctx.textAlign = "center";
  indexes.forEach((pointIndex) => {
    const x = padding.left + (points.length === 1 ? plotWidth / 2 : (pointIndex / (points.length - 1)) * plotWidth);
    ctx.fillText(formatShortDateTime(points[pointIndex].timestamp), x, padding.top + plotHeight + 20);
  });
  ctx.textAlign = "left";
}

function drawPointValueDetails(ctx, points, xOf, yOf) {
  const shouldLabelAll = points.length <= 18;
  ctx.font = "10px Segoe UI, Arial";
  ctx.textAlign = "center";
  points.forEach((point, index) => {
    if (!shouldLabelAll && index !== 0 && index !== points.length - 1 && !point.hasRuleViolation) return;
    const x = xOf(point, index);
    const y = yOf(point, index);
    ctx.fillStyle = point.hasRuleViolation ? "#b42318" : "#344054";
    ctx.fillText(formatNumber(point.value), x, Math.max(12, y - 8));
  });
  ctx.textAlign = "left";
}

function histogramBins(points, low, high) {
  const values = points.map((point) => Number(point.value));
  const binCount = Math.min(8, Math.max(4, Math.ceil(Math.sqrt(values.length))));
  const binWidth = (high - low) / binCount || 1;
  const bins = Array.from({ length: binCount }, (_, index) => ({
    count: 0,
    low: low + index * binWidth,
    high: low + (index + 1) * binWidth
  }));
  values.forEach((value) => {
    const index = Math.min(binCount - 1, Math.max(0, Math.floor((value - low) / binWidth)));
    bins[index].count += 1;
  });
  return bins;
}

function drawHistogramDetails(ctx, points, padding, plotWidth, plotHeight, low, high) {
  const bins = histogramBins(points, low, high);
  const maxBin = Math.max(...bins.map((bin) => bin.count), 1);
  drawYAxisDetails(ctx, padding, plotWidth, plotHeight, 0, maxBin);
  ctx.fillStyle = "#344054";
  ctx.font = "10px Segoe UI, Arial";
  ctx.textAlign = "center";
  bins.forEach((bin, index) => {
    const x = padding.left + (index + 0.5) * (plotWidth / bins.length);
    const y = padding.top + plotHeight - (bin.count / maxBin) * plotHeight;
    ctx.fillText(String(bin.count), x, y - 5);
    ctx.fillText(`${formatNumber(bin.low)}-${formatNumber(bin.high)}`, x, padding.top + plotHeight + 18);
  });
  ctx.textAlign = "left";
}

function drawMovingRangeDetails(ctx, points, padding, plotWidth, plotHeight) {
  const ranges = points.map((point) => Number(point.movingRange)).filter(Number.isFinite);
  if (!ranges.length) return;
  const high = Math.max(...ranges, 1) * 1.1;
  drawYAxisDetails(ctx, padding, plotWidth, plotHeight, 0, high);
  ctx.fillStyle = "#344054";
  ctx.font = "10px Segoe UI, Arial";
  ctx.textAlign = "center";
  points.forEach((point, index) => {
    const range = Number(point.movingRange);
    if (!Number.isFinite(range)) return;
    if (points.length > 18 && index !== points.length - 1 && !point.hasRuleViolation) return;
    const x = padding.left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
    const y = padding.top + (1 - (range / high)) * plotHeight;
    ctx.fillText(formatNumber(range), x, Math.max(12, y - 8));
  });
  ctx.textAlign = "left";
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
        <span>${user.roles.join(", ")} / ${user.productGroups?.length ? user.productGroups.join(", ") : "No product groups"}</span>
      </div>
      <div class="row-actions">
        <button type="button" class="secondary compact-button user-edit-button">Edit</button>
        <button type="button" class="secondary compact-button danger-button user-delete-button">Delete</button>
      </div>`;
    row.querySelector(".user-edit-button").addEventListener("click", () => {
      $("setupUserName").value = user.userName;
      $("setupPassword").value = "";
      $("setupRole").value = user.roles[0] || state.roles[0] || "";
      $("setupUserProductGroups").value = (user.productGroups || []).join(", ");
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
        roles: [$("setupRole").value],
        productGroups: parseCommaList($("setupUserProductGroups").value)
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

function setupJobDataFieldTemplate() {
  return `
    <label><span>Field name</span><input class="setup-job-data-field-name" required></label>
    <label>
      <span>Required</span>
      <select class="setup-job-data-required">
        <option value="true">Yes</option>
        <option value="false">No</option>
      </select>
    </label>
    <button type="button" class="secondary compact-button remove-job-data-field-button">Remove</button>`;
}

function addSetupJobDataFieldRow(values = {}) {
  const row = document.createElement("div");
  row.className = "setup-job-data-field-row";
  row.dataset.originalFieldName = values.fieldName || "";
  row.innerHTML = setupJobDataFieldTemplate();
  row.querySelector(".setup-job-data-field-name").value = values.fieldName || "";
  row.querySelector(".setup-job-data-required").value = String(values.isRequired ?? true);
  row.querySelector(".remove-job-data-field-button").addEventListener("click", () => row.remove());
  $("setupJobDataFieldRows").appendChild(row);
}

function addSetupVariableRow(values = {}, type = values.characteristicType || "Variable") {
  const row = document.createElement("div");
  row.className = "setup-variable-row";
  row.dataset.originalCharacteristicName = values.characteristicName || "";
  row.innerHTML = setupVariableRowTemplate();
  row.querySelector(".setup-characteristic-name").value = values.characteristicName || "";
  row.querySelector(".setup-characteristic-type").value = type;
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
    const container = row.parentElement;
    if (container?.children.length === 1 && container.id === "setupVariableRows") {
      row.querySelectorAll("input").forEach((input) => { input.value = ""; });
      row.querySelector(".setup-coa-required").value = "true";
      row.querySelector(".setup-coa-statistic").value = "Mean";
      return;
    }
    row.remove();
  });
  $(type === "Attribute" ? "setupAttributeRows" : "setupVariableRows").appendChild(row);
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
  $("setupProductGroup").value = set.productGroup || "General";
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
  $("setupAttributeRows").innerHTML = "";
  set.plans.forEach((plan) => addSetupVariableRow(plan, plan.characteristicType));
  $("setupJobDataFieldRows").innerHTML = "";
  (state.snapshot.partJobDataFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(firstPlan.inspectionPhase))
    .forEach((field) => addSetupJobDataFieldRow(field));
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
  $("setupProductGroup").value = "General";
  $("setupFrequencyType").value = "Quantity";
  $("setupFrequencyValue").value = "10000";
  $("setupFrequencyUnit").value = "Pieces";
  $("setupAlertRuleSet").value = "GlobalDefault";
  $("setupInspectionPhase").value = "In Process";
  updateRuleDescription();
  updateSetupFrequencyUnits();
  $("setupVariableRows").innerHTML = "";
  $("setupAttributeRows").innerHTML = "";
  $("setupJobDataFieldRows").innerHTML = "";
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
    Custom: "Uses the saved custom protocol from the Rules tab. Click Custom Rule in Rules to review or configure the protocol.",
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
      body: JSON.stringify({
        globalAlertRuleSet: $("globalAlertRuleSet").value,
        customDriftRule: customRulePayload()
      })
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

function loadCustomRuleForm() {
  const rule = state.snapshot?.settings?.customDriftRule || defaultCustomRule();
  $("customRuleName").value = rule.name || "Custom Drift Rule";
  $("customWindowSize").value = String(rule.windowSize || 4);
  $("customSigmaThreshold").value = String(rule.sigmaThreshold || 1);
  $("customMinimumPoints").value = String(rule.minimumPointsBeyondThreshold || 4);
  $("customDirection").value = rule.direction || "SameSide";
  $("customIncludeWesternElectric").value = String(rule.includeWesternElectric ?? false);
  $("customWarningBehavior").value = rule.warningBehavior || "Lock";
  $("customRuleNotes").value = rule.notes || "";
}

function defaultCustomRule() {
  return {
    name: "Custom Drift Rule",
    windowSize: 4,
    sigmaThreshold: 1,
    minimumPointsBeyondThreshold: 4,
    direction: "SameSide",
    includeWesternElectric: false,
    warningBehavior: "Lock",
    notes: "Triggers when the configured number of recent points are beyond the configured sigma threshold."
  };
}

function customRulePayload() {
  return {
    name: $("customRuleName").value.trim() || "Custom Drift Rule",
    windowSize: Number($("customWindowSize").value),
    sigmaThreshold: Number($("customSigmaThreshold").value),
    minimumPointsBeyondThreshold: Number($("customMinimumPoints").value),
    direction: $("customDirection").value,
    includeWesternElectric: $("customIncludeWesternElectric").value === "true",
    warningBehavior: $("customWarningBehavior").value,
    notes: $("customRuleNotes").value.trim()
  };
}

function openRuleDetail(ruleKey) {
  const details = RULE_DETAILS[ruleKey] || RULE_DETAILS.WesternElectric;
  $("ruleDetailTitle").textContent = details.title;
  $("ruleDetailSubtitle").textContent = details.subtitle;
  $("ruleDetailBody").innerHTML = details.body;
  $("customRuleForm").classList.toggle("hidden", ruleKey !== "Custom");
  $("customRuleMessage").textContent = "";
  $("customRuleMessage").className = "message";
  if (ruleKey === "Custom") {
    loadCustomRuleForm();
  }
  $("ruleDetailModal").classList.remove("hidden");
}

function closeRuleDetail() {
  $("ruleDetailModal").classList.add("hidden");
}

async function saveCustomRule(event) {
  event.preventDefault();
  try {
    const settings = await api("/setup/settings", {
      method: "POST",
      body: JSON.stringify({
        globalAlertRuleSet: $("globalAlertRuleSet").value,
        customDriftRule: customRulePayload()
      })
    });
    state.snapshot.settings = settings;
    loadCustomRuleForm();
    $("customRuleMessage").textContent = "Custom drift protocol saved.";
    $("customRuleMessage").className = "message ok";
    $("globalRuleMessage").textContent = "Custom drift protocol saved.";
    $("globalRuleMessage").className = "message ok";
  } catch (error) {
    $("customRuleMessage").textContent = readableError(error);
    $("customRuleMessage").className = "message error";
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

function setupJobDataFieldRows() {
  return [...document.querySelectorAll(".setup-job-data-field-row")]
    .map((row, index) => ({
      originalFieldName: row.dataset.originalFieldName || null,
      fieldName: row.querySelector(".setup-job-data-field-name").value.trim(),
      isRequired: row.querySelector(".setup-job-data-required").value === "true",
      displayOrder: index
    }))
    .filter((row) => row.fieldName);
}

function optionalInputNumber(input) {
  const value = input.value.trim();
  return value ? Number(value) : null;
}

async function saveInspectionSetup(event) {
  event.preventDefault();
  const variables = setupVariableRows();
  const jobDataFields = setupJobDataFieldRows();
  if (!variables.length || variables.some((variable) => !variable.characteristicName)) {
    $("inspectionSetupMessage").textContent = "Add at least one measurement name.";
    $("inspectionSetupMessage").className = "message error";
    return;
  }

  try {
    const baseRequest = {
      partNum: $("setupPartNum").value.trim(),
      partDescription: $("setupPartDescription").value.trim(),
      productGroup: $("setupProductGroup").value.trim(),
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

    for (const field of jobDataFields) {
      await api("/setup/job-data-fields", {
        method: "POST",
        body: JSON.stringify({
          partNum: baseRequest.partNum,
          inspectionPhase: baseRequest.inspectionPhase,
          fieldName: field.fieldName,
          isRequired: field.isRequired,
          displayOrder: field.displayOrder,
          originalFieldName: field.originalFieldName
        })
      });
    }

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
    "PartNum,PartDescription,ProductGroup,ProcessCode,ProcessDescription,OperationSeq,CharacteristicName,CharacteristicType,Nominal,LSL,USL,LCL,UCL,UnitOfMeasure,InspectionPhase,SampleSize,FrequencyType,FrequencyValue,FrequencyUnit,AlertRuleSet,IsRequiredForCOA,COAStatistic",
    "P200,Example part,General,MOLD,Molding,10,Measurement 1,Variable,5.0,4.5,5.5,4.4,5.6,mm,Startup,5,Event,1,StartOfJob,WesternElectric,true,Mean",
    "P200,Example part,General,MOLD,Molding,10,Measurement 2,Variable,42.0,41.5,42.5,41.0,43.0,mm,In Process,5,Quantity,10000,Pieces,NelsonRules,true,StandardDeviation"
  ].join("\n");
}

function parseCommaList(value) {
  return value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function readableError(error) {
  try {
    const parsed = JSON.parse(error.message);
    return parsed.errors?.join(" ") || error.message;
  } catch {
    return error.message;
  }
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
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

function formatShortDateTime(value) {
  return new Date(value).toLocaleString([], {
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
$("jobTagsForm").addEventListener("submit", saveJobTags);
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
$("setupReportsSectionTab").addEventListener("click", () => showSetupSection("Reports"));
$("setupJobDataSectionTab").addEventListener("click", () => showSetupSection("JobData"));
$("userSetupForm").addEventListener("submit", saveUser);
$("inspectionSetupForm").addEventListener("submit", saveInspectionSetup);
$("addSetupVariableButton").addEventListener("click", () => addSetupVariableRow());
$("addSetupAttributeButton").addEventListener("click", () => addSetupVariableRow({}, "Attribute"));
$("addSetupJobDataFieldButton").addEventListener("click", () => addSetupJobDataFieldRow());
$("clearInspectionSetupButton").addEventListener("click", clearInspectionSetupForm);
$("loadPartSetupButton").addEventListener("click", loadSelectedPartSetup);
$("setupFrequencyType").addEventListener("change", updateSetupFrequencyUnits);
$("setupAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalRuleForm").addEventListener("submit", saveGlobalRule);
document.querySelectorAll(".rule-card[data-rule-key]").forEach((card) => {
  card.addEventListener("click", () => openRuleDetail(card.dataset.ruleKey));
});
$("closeRuleDetailButton").addEventListener("click", closeRuleDetail);
$("ruleDetailModal").addEventListener("click", (event) => {
  if (event.target.id === "ruleDetailModal") {
    closeRuleDetail();
  }
});
$("customRuleForm").addEventListener("submit", saveCustomRule);
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
$("reportForm").addEventListener("submit", runReport);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");
clearInspectionSetupForm();

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}


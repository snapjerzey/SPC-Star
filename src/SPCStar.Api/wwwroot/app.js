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
  editingSetup: null,
  selectedUserName: ""
};

const $ = (id) => document.getElementById(id);

async function api(path, options = {}) {
  const isFormData = options.body instanceof FormData;
  const response = await fetch(path, {
    headers: { ...(isFormData ? {} : { "Content-Type": "application/json" }), ...(options.headers || {}) },
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
  const operationKey = $("operationCode").value;
  const phase = $("inspectionPhase").value;
  const activeSet = inspectionSets().find((set) =>
    set.partNum.toLowerCase() === partNum.toLowerCase() &&
    operationKeyFor(set) === operationKey &&
    normalizeInspectionPhase(set.inspectionPhase) === normalizeInspectionPhase(phase)) || null;
  const partPlans = (state.snapshot?.inspectionPlans || [])
    .filter((plan) =>
      plan.partNum.toLowerCase() === partNum.toLowerCase() &&
      operationKeyFor(plan) === operationKey);
  if (!partPlans.length) {
    return null;
  }

  const base = activeSet || inspectionSets().find((set) =>
    set.partNum.toLowerCase() === partNum.toLowerCase() &&
    operationKeyFor(set) === operationKey);
  return {
    ...base,
    inspectionPhase: phase,
    activePhase: phase,
    plans: displayPlansForPhase(partPlans, phase)
  };
}

function operationKeyFor(item) {
  return `${item.processCode}|${item.operationSeq}`;
}

function operationLabelFor(item) {
  const description = item.processDescription && item.processDescription !== item.processCode
    ? ` - ${item.processDescription}`
    : "";
  return `${item.processCode}${description}`;
}

function operationsForPart(partNum) {
  const operations = new Map();
  (state.snapshot?.inspectionPlans || [])
    .filter((plan) => plan.partNum.toLowerCase() === partNum.toLowerCase())
    .forEach((plan) => {
      const key = operationKeyFor(plan);
      if (!operations.has(key)) {
        operations.set(key, plan);
      }
    });
  return [...operations.values()]
    .sort((a, b) => a.processCode.localeCompare(b.processCode) || (a.operationSeq ?? 0) - (b.operationSeq ?? 0));
}

function refreshOperationChoices({ preserve = true } = {}) {
  const select = $("operationCode");
  const previous = preserve ? select.value : "";
  const partNum = $("partNum").value.trim();
  const operations = partNum ? operationsForPart(partNum) : [];
  fillSelect(select, [{ processCode: "", operationSeq: "", processDescription: "Select operation" }, ...operations],
    (operation) => operation.processCode ? operationKeyFor(operation) : "",
    (operation) => operation.processCode ? operationLabelFor(operation) : operation.processDescription);
  if (operations.some((operation) => operationKeyFor(operation) === previous)) {
    select.value = previous;
  } else if (operations.length === 1) {
    select.value = operationKeyFor(operations[0]);
  } else {
    select.value = "";
  }
}

function displayPlansForPhase(plans, phase) {
  const activePhase = normalizeInspectionPhase(phase);
  return plans
    .filter((plan) => normalizeInspectionPhase(plan.inspectionPhase) === activePhase)
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0) || a.characteristicName.localeCompare(b.characteristicName))
    .map((plan) => ({
      ...plan,
      isActiveForSelectedPhase: true,
      selectedInspectionPhase: phase
    }));
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

function toggleChangePassword() {
  $("changePasswordPanel").classList.toggle("hidden");
  $("changePasswordMessage").textContent = "";
  $("changePasswordMessage").className = "message";
  $("changePasswordUserName").value = $("userName").value.trim();
}

async function changePassword() {
  try {
    await api("/auth/change-password", {
      method: "POST",
      body: JSON.stringify({
        userName: $("changePasswordUserName").value.trim(),
        currentPassword: $("currentPassword").value,
        newPassword: $("newPassword").value,
        confirmPassword: $("confirmPassword").value
      })
    });
    $("password").value = $("newPassword").value;
    $("currentPassword").value = "";
    $("newPassword").value = "";
    $("confirmPassword").value = "";
    $("changePasswordMessage").textContent = "Password changed.";
    $("changePasswordMessage").className = "message ok";
  } catch (error) {
    $("changePasswordMessage").textContent = readableError(error);
    $("changePasswordMessage").className = "message error";
  }
}

async function loadSnapshot() {
  state.snapshot = await api("/sync/setup-snapshot");
  fillDatalist($("jobOptions"), state.snapshot.jobs, (job) => job.jobNum);
  $("jobNum").value = "";
  fillSelect($("resourceId"), [{ resourceId: "", description: "Select machine" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillDatalist($("partOptions"), state.snapshot.parts, (part) => part.partNum);
  fillDatalist($("productGroupOptions"), productGroups(), (group) => group);
  $("partNum").value = "";
  refreshOperationChoices({ preserve: false });
  if (canManageSetup()) {
    renderGlobalRuleSetting();
    renderPartReviewControls();
    renderReportControls();
    renderSetupEditChoices();
    renderUserProductGroupPicker();
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
  if (phase === "coil change" || phase === "coilchange") return "Coil Change";
  if (phase === "spool" || phase === "spool start" || phase === "spool end") return "Spool";
  return "In Process";
}

function updatePartFromJob() {
  const job = state.snapshot.jobs.find((item) => item.jobNum.toLowerCase() === $("jobNum").value.trim().toLowerCase());
  if (job && state.snapshot.parts.some((part) => part.partNum.toLowerCase() === job.partNum.toLowerCase())) {
    $("partNum").value = job.partNum;
    refreshOperationChoices({ preserve: false });
  }
}

async function loadContext(event) {
  event?.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  const partNum = $("partNum").value.trim();
  const operationKey = $("operationCode").value;
  if (!jobNum || !resourceId || !partNum || !operationKey) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext();
    return;
  }

  if (!set) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext(`Part ${partNum} is not set up. Ask Admin or GOD to add the inspection plan before inspecting.`);
    return;
  }

  if (!set.plans.length) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext(`No inspection items are required for ${$("inspectionPhase").value} on ${partNum} / ${operationLabelFor(set)}.`);
    return;
  }

  state.selectedPlans = set.plans;
  state.contexts = await Promise.all(set.plans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  renderContext();
}

function renderEmptyContext(message = "") {
  $("contextTitle").textContent = "Inspection Items";
  $("contextSubtitle").textContent = "Enter a job number, machine, part number, and operation, then start inspecting.";
  renderLock(null);
  $("measurementForm").classList.add("hidden");
  $("trendPanel").classList.add("hidden");
  $("jobNotesPanel").classList.add("hidden");
  $("materialPanel").classList.add("hidden");
  $("tagsDivider").classList.add("hidden");
  $("tagsSection").classList.add("hidden");
  $("measurementVariableList").innerHTML = "";
  $("meanSummary").innerHTML = "";
  $("trendCharacteristic").innerHTML = "";
  $("entryMessage").textContent = message;
  $("entryMessage").className = message ? "message error" : "message";
  $("jobTagsForm").innerHTML = "";
  $("jobTagsForm").classList.add("hidden");
  $("tagMessage").textContent = "";
  $("materialFieldRows").innerHTML = "";
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
  $("contextTitle").textContent = "Inspection Items";
  $("contextSubtitle").textContent = `${jobNum} / ${resourceId} / ${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.activePhase || set.inspectionPhase}`;
  $("measurementForm").classList.remove("hidden");
  $("trendPanel").classList.remove("hidden");
  $("jobNotesPanel").classList.remove("hidden");
  $("materialPanel").classList.remove("hidden");
  renderConfiguredJobDataFields(set);
  renderConfiguredMaterialFields(set);
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

function renderConfiguredMaterialFields(set) {
  const fields = (state.snapshot.partMaterialFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase())
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
  const rows = $("materialFieldRows");
  const materialFields = fields.length ? fields : [{
    materialName: "Material",
    materialPartNum: "",
    materialDescription: "",
    isRequired: true,
    displayOrder: 0
  }];
  rows.innerHTML = materialFields.map((field, index) => `
    <section class="material-field-row">
      <h3>${escapeHtml(field.materialName)}${field.materialDescription ? ` - ${escapeHtml(field.materialDescription)}` : ""}</h3>
      <label>
        Material part number
        <input class="material-part-input" data-material-index="${index}" autocomplete="off" inputmode="text" value="${escapeHtml(field.materialPartNum || "")}" ${field.isRequired ? "required" : ""}>
      </label>
      <label>
        New lot number
        <input class="material-lot-input" data-material-index="${index}" autocomplete="off" inputmode="text" ${field.isRequired ? "required" : ""}>
      </label>
      <label>
        Reason
        <select class="material-reason-input" data-material-index="${index}" required>
          <option value="Material change">Material change</option>
          <option value="Material issue at job start">Material issue at job start</option>
        </select>
      </label>
    </section>`).join("");
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
  measurementList.innerHTML = "";
  state.selectedPlans.forEach((plan, index) => {
    const context = state.contexts[index];
    const card = document.createElement("section");
    const isInactive = plan.isActiveForSelectedPhase === false;
    card.className = `variable-card${isInactive ? " inactive-plan-card" : ""}`;
    const isAttribute = plan.characteristicType === "Attribute";
    const isRecordOnly = !isAttribute && !hasSpecLimits(plan, context);
    card.innerHTML = `
      <div>
        <div class="variable-header">
          <div class="variable-title">
            <strong>${plan.characteristicName}</strong>
            <span>${isAttribute ? "Accept / Reject" : isRecordOnly ? `Record only${plan.unitOfMeasure ? ` (${plan.unitOfMeasure})` : ""}` : plan.unitOfMeasure}</span>
          </div>
          <div class="sample-meta">
            ${isInactive ? `
              <span class="inactive-required-badge">Not required for ${escapeHtml(plan.selectedInspectionPhase || $("inspectionPhase").value)}</span>` : `
              <span>${plan.inspectionPhase || "In Process"}</span>
              <span>Sample size ${plan.sampleSize}</span>
              <span>${formatFrequency(plan)}</span>`}
          </div>
        </div>
        ${isAttribute ? `
          <div class="attribute-note">Comparator/template check</div>` : `
          ${isRecordOnly ? `
          <div class="record-only-note">Record only - no specification limits or capability calculations are applied.</div>` : `
          <div class="limit-grid">
            <div><span>LSL</span><strong>${formatNumber(context.lowerSpecLimit)}</strong></div>
            <div><span>Target</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>USL</span><strong>${formatNumber(context.upperSpecLimit)}</strong></div>
            <div><span>LCL</span><strong>${formatNumber(context.lowerControlLimit)}</strong></div>
            <div><span>Center</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>UCL</span><strong>${formatNumber(context.upperControlLimit)}</strong></div>
          </div>`}`}
      </div>
      ${isInactive ? `
        <div class="inactive-plan-note">This item is part of the full inspection plan, but it is not entered during this inspection type.</div>` : `
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
        </div>`}`;
    if (isInactive) {
      card.querySelectorAll(".measurement-input").forEach((input) => {
        input.disabled = true;
        input.title = `Not required for ${plan.selectedInspectionPhase || $("inspectionPhase").value}`;
      });
    }
    measurementList.appendChild(card);
  });
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
    const isRecordOnly = !hasSpecLimits(plan, state.contexts[index]);
    item.innerHTML = `
      <span>${plan.characteristicName}</span>
      <span>${formatNumber(values.length ? Math.min(...values) : null)}</span>
      <span>${formatNumber(values.length ? Math.max(...values) : null)}</span>
      <span>${formatNumber(mean)}</span>
      <span>${formatNumber(standardDeviation(values))}</span>
      ${isRecordOnly ? `
      <span class="record-only-cell">Record only</span>` : `
      <span>${capabilityBadge(capability.cp)}</span>
      <span>${capabilityBadge(capability.cpk)}</span>
      <span>${capabilityBadge(capability.pp)}</span>
      <span>${capabilityBadge(capability.ppk)}</span>`}`;
    summary.appendChild(item);
  });
}

function hasSpecLimits(plan, context) {
  return isFiniteValue(plan?.lsl) ||
    isFiniteValue(plan?.usl) ||
    isFiniteValue(plan?.nominal) ||
    isFiniteValue(context?.lowerSpecLimit) ||
    isFiniteValue(context?.upperSpecLimit);
}

function isFiniteValue(value) {
  return value !== null && value !== undefined && value !== "" && Number.isFinite(Number(value));
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
  const thresholds = capabilityThresholds();
  if (Number(value) >= thresholds.greenMinimum) return "capability-good";
  if (Number(value) >= thresholds.yellowMinimum) return "capability-warn";
  return "capability-bad";
}

function capabilityThresholds() {
  const settings = state.snapshot?.settings?.capabilityThresholds || {};
  return {
    yellowMinimum: Number(settings.yellowMinimum ?? 1.00),
    greenMinimum: Number(settings.greenMinimum ?? 1.33)
  };
}

function formatFrequency(plan) {
  const unit = {
    Minutes: "minutes",
    Hours: "hours",
    Pieces: "parts",
    StartOfJob: "start of job",
    MaterialChange: "material change",
    ToolChange: "tool change",
    Restart: "restart",
    Shift: "shift"
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

async function saveMaterialChange(event) {
  event.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  const entries = [...document.querySelectorAll(".material-field-row")]
    .map((row) => ({
      materialPartNum: row.querySelector(".material-part-input").value.trim(),
      newLotNum: row.querySelector(".material-lot-input").value.trim(),
      reason: row.querySelector(".material-reason-input").value
    }))
    .filter((entry) => entry.materialPartNum || entry.newLotNum);
  if (!jobNum || !resourceId || !set) {
    $("materialMessage").textContent = "Start work before saving a material lot.";
    $("materialMessage").className = "message error";
    return;
  }

  if (!entries.length || entries.some((entry) => !entry.materialPartNum || !entry.newLotNum)) {
    $("materialMessage").textContent = "Material part number and new lot number are required for each material entry.";
    $("materialMessage").className = "message error";
    return;
  }

  try {
    for (const entry of entries) {
      await api("/material-changes", {
        method: "POST",
        body: JSON.stringify({
          jobNum,
          partNum: set.partNum,
          materialPartNum: entry.materialPartNum,
          oldLotNum: "",
          newLotNum: entry.newLotNum,
          quantityLoaded: null,
          resourceId,
          operatorUserId: state.user.userName,
          timestamp: new Date().toISOString(),
          reason: entry.reason,
          deviceId: "browser-dev",
          clientRecordId: newClientRecordId(),
          submittedAt: new Date().toISOString()
        })
      })
    }
    document.querySelectorAll(".material-lot-input").forEach((input) => {
      input.value = "";
    });
    $("materialMessage").textContent = `${entries.length} material lot${entries.length === 1 ? "" : "s"} saved.`;
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
  renderUserProductGroupPicker();
  renderUsers();
  if (!$("setupVariableRows").children.length) {
    addSetupVariableRow();
  }
}

function renderPartReviewControls() {
  $("partReviewFilter").placeholder = "Part number";
}

function renderReportControls() {
  refreshReportOperationChoices();
  fillSelect($("reportResourceId"), [{ resourceId: "", description: "All machines" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillDatalist($("reportCharacteristicOptions"), state.snapshot.characteristics, (characteristic) => characteristic.name);
}

function refreshReportOperationChoices() {
  const partNum = reportPartFilter();
  const operations = partNum ? operationsForPart(partNum) : [];
  fillSelect(
    $("reportOperationCode"),
    [{ processCode: "", operationSeq: "", processDescription: "All operations" }, ...operations],
    (operation) => operation.processCode ? operationKeyFor(operation) : "",
    (operation) => operation.processCode ? operationLabelFor(operation) : operation.processDescription);
}

async function loadReview() {
  const partNum = $("partReviewFilter").value.trim();
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
  renderCapabilityThresholds();
  updateRuleDescription();
  loadCustomRuleForm();
}

function renderCapabilityThresholds() {
  const thresholds = capabilityThresholds();
  $("capabilityYellowMinimum").value = thresholds.yellowMinimum.toFixed(2);
  $("capabilityGreenMinimum").value = thresholds.greenMinimum.toFixed(2);
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
      <span>Scope</span><span>Operation</span><span>Phase</span><span>Variable</span><span>Type</span><span>Mean</span><span>Std Dev</span><span>Cp</span><span>Cpk</span><span>Pp</span><span>Ppk</span><span>Count</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.processCode || ""} ${row.operationSeq || ""}</span>
      <span>${row.inspectionPhases || ""}</span>
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
      <span>Job</span><span>Context</span><span>Variable</span><span>COA</span><span>Mean</span><span>Range</span><span>Std Dev</span><span>Capability</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.processCode || ""} ${row.operationSeq || ""}<small>${row.inspectionPhases || ""}</small></span>
      <span>${row.characteristicName} (${row.unitOfMeasure})</span>
      <span>${coaStatisticLabel(row.coaStatisticType)}<small>${formatNumber(row.coaValue)}</small></span>
      <span>${formatNumber(row.mean)}</span>
      <span>${formatNumber(row.min)} - ${formatNumber(row.max)}</span>
      <span>${formatNumber(row.stdDev)}</span>
      <span>Cpk ${capabilityBadge(row.cpk)}<small>Ppk ${capabilityBadge(row.ppk)}</small></span>`;
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
    const candidates = reportCharacteristicCandidates();
    const results = [];
    for (const characteristicName of candidates) {
      const data = await api("/charts/data", {
        method: "POST",
        body: JSON.stringify(reportRequest(characteristicName))
      });
      if (data.points.length || $("reportCharacteristicName").value.trim()) {
        results.push({ characteristicName, data });
      }
    }

    if (!results.length) {
      const data = await api("/charts/data", {
        method: "POST",
        body: JSON.stringify(reportRequest(candidates[0] || null))
      });
      results.push({ characteristicName: candidates[0] || $("reportCharacteristicName").value.trim() || "No matching variable", data });
    }

    renderReportCharts(results);
    const pointCount = results.reduce((total, result) => total + result.data.points.length, 0);
    $("reportMessage").textContent = `${results.length} chart${results.length === 1 ? "" : "s"} / ${pointCount} point${pointCount === 1 ? "" : "s"} loaded.`;
    $("reportMessage").className = "message ok";
  } catch (error) {
    $("reportMessage").textContent = readableError(error);
    $("reportMessage").className = "message error";
  }
}

function reportRequest(characteristicName) {
  const operation = selectedReportOperation();
  return {
    chartType: "IndividualsMovingRange",
    jobNum: $("reportJobNum").value.trim() || null,
    partNum: $("reportPartNum").value.trim() || null,
    processCode: operation?.processCode || null,
    operationSeq: operation?.operationSeq || null,
    resourceId: $("reportResourceId").value || null,
    characteristicName,
    from: dateTimeLocalValue("reportFrom"),
    to: dateTimeLocalValue("reportTo"),
    inspectionPhase: $("reportInspectionPhase").value || null
  };
}

function reportCharacteristicCandidates() {
  const entered = $("reportCharacteristicName").value.trim();
  if (entered) {
    return [entered];
  }

  const partFilter = reportPartFilter();
  const operation = selectedReportOperation();
  const phaseFilter = $("reportInspectionPhase").value;
  const plans = state.snapshot.inspectionPlans.filter((plan) =>
    (!partFilter || plan.partNum.toLowerCase() === partFilter.toLowerCase()) &&
    (!operation || operationKeyFor(plan) === operationKeyFor(operation)) &&
    (!phaseFilter || normalizeInspectionPhase(plan.inspectionPhase) === normalizeInspectionPhase(phaseFilter)));
  const names = plans.map((plan) => plan.characteristicName);
  const fallback = state.snapshot.characteristics.map((characteristic) => characteristic.name);
  return [...new Set((names.length ? names : fallback).filter(Boolean))].sort();
}

function reportPartFilter() {
  const reportPart = $("reportPartNum").value.trim();
  const reportJob = $("reportJobNum").value.trim();
  const jobPart = state.snapshot.jobs.find((job) => job.jobNum.toLowerCase() === reportJob.toLowerCase())?.partNum || "";
  return reportPart || jobPart;
}

function selectedReportOperation() {
  const value = $("reportOperationCode").value;
  if (!value) {
    return null;
  }

  const [processCode, operationSeqText] = value.split("|");
  return {
    processCode,
    operationSeq: Number(operationSeqText)
  };
}

function renderReportCharts(results) {
  const grid = $("reportChartGrid");
  grid.innerHTML = "";
  results.forEach((result) => {
    const card = document.createElement("section");
    card.className = "report-chart-card";
    card.innerHTML = `
      <h3>${escapeHtml(result.characteristicName)}</h3>
      <canvas class="trend-canvas" width="940" height="260"></canvas>`;
    grid.appendChild(card);
    drawReport(result.data.points, result.data, {
      canvas: card.querySelector("canvas"),
      characteristicName: result.characteristicName
    });
  });
}

function dateTimeLocalValue(id) {
  const value = $(id).value;
  return value ? new Date(value).toISOString() : null;
}

function drawReport(points, data = {}, options = {}) {
  const canvas = options.canvas;
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
  drawReportHeader(ctx, width, points, data, chartType, options.characteristicName);
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

function drawReportHeader(ctx, width, points, data, chartType, characteristicName) {
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
    selectedReportOperation() ? $("reportOperationCode").selectedOptions[0]?.textContent || "Selected operation" : "All operations",
    characteristicName || $("reportCharacteristicName").value.trim() || "All variables",
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
    renderUserDetail(null);
    return;
  }

  list.className = "setup-list";
  list.innerHTML = "";
  const selectedExists = state.users.some((user) => user.userName === state.selectedUserName);
  if (state.selectedUserName && !selectedExists) {
    state.selectedUserName = "";
  }
  state.users.forEach((user) => {
    const row = document.createElement("div");
    row.className = `setup-row user-list-row ${user.userName === state.selectedUserName ? "selected" : ""}`;
    const productGroupText = user.productGroups?.length ? user.productGroups.join(", ") : "No product groups assigned";
    row.innerHTML = `
      <div>
        <strong>${user.userName}</strong>
        <span>${user.roles.join(", ")}</span>
        <small>${productGroupText}</small>
      </div>`;
    row.addEventListener("click", () => selectUser(user.userName));
    list.appendChild(row);
  });

  if (state.selectedUserName) {
    renderUserDetail(state.users.find((user) => user.userName === state.selectedUserName) || null);
  } else {
    renderUserDetail(null);
  }
}

function selectUser(userName) {
  state.selectedUserName = userName;
  const user = state.users.find((item) => item.userName === userName) || null;
  renderUsers();
  renderUserDetail(user);
}

function newUser() {
  state.selectedUserName = "";
  renderUsers();
  renderUserDetail({
    userName: "",
    roles: [state.roles[0] || ""],
    productGroups: []
  }, true);
}

function renderUserDetail(user, isNew = false) {
  const panel = $("userDetailPanel");
  const hasUser = Boolean(user);
  panel.classList.toggle("empty", !hasUser);
  $("userSetupForm").querySelector(".user-detail-grid").classList.toggle("hidden", !hasUser);
  $("userSetupForm").querySelector(".permission-panel").classList.toggle("hidden", !hasUser);
  $("userDetailTitle").textContent = isNew ? "New User" : hasUser ? user.userName : "Select a user";
  $("userDetailSubtitle").textContent = isNew
    ? "Create the account, set the role, and choose approved product groups."
    : hasUser
      ? "Edit role and product group access. Use reset only when a password is forgotten."
      : "Choose a user from the list or create a new account.";
  $("setupUserName").value = user?.userName || "";
  $("setupUserName").disabled = Boolean(hasUser && !isNew);
  $("setupPassword").value = "";
  $("setupPasswordLabel").classList.toggle("hidden", hasUser && !isNew);
  $("setupRole").value = user?.roles?.[0] || state.roles[0] || "";
  setUserProductGroupSelection(user?.productGroups || []);
  $("resetSelectedUserPasswordButton").classList.toggle("hidden", !hasUser || isNew);
  $("deleteSelectedUserButton").classList.toggle("hidden", !hasUser || isNew);
  $("userSetupForm").querySelector("button[type='submit']").classList.toggle("hidden", !hasUser);
}

function renderUserProductGroupPicker(selectedGroups = selectedUserProductGroups()) {
  const picker = $("setupUserProductGroups");
  const groups = productGroups();
  if (!groups.length) {
    picker.className = "product-group-picker empty";
    picker.textContent = "No product groups loaded.";
    return;
  }

  picker.className = "product-group-picker";
  picker.innerHTML = "";
  groups.forEach((group) => {
    const option = document.createElement("label");
    option.className = "product-group-option";
    option.innerHTML = `
      <input type="checkbox" value="${escapeHtml(group)}">
      <span>${escapeHtml(group)}</span>`;
    option.querySelector("input").checked = selectedGroups.includes(group);
    picker.appendChild(option);
  });
}

function selectedUserProductGroups() {
  return [...$("setupUserProductGroups").querySelectorAll("input[type='checkbox']:checked")]
    .map((input) => input.value);
}

function setUserProductGroupSelection(groups) {
  const selected = new Set(groups);
  $("setupUserProductGroups").querySelectorAll("input[type='checkbox']").forEach((input) => {
    input.checked = selected.has(input.value);
  });
}

async function deleteUser(userName) {
  if (!userName) {
    $("userSetupMessage").textContent = "Select a user to delete.";
    $("userSetupMessage").className = "message error";
    return;
  }

  if (!window.confirm(`Delete ${userName}?`)) {
    return;
  }

  try {
    await api(`/setup/users/${encodeURIComponent(userName)}`, { method: "DELETE" });
    $("userSetupMessage").textContent = `${userName} deleted.`;
    $("userSetupMessage").className = "message ok";
    state.selectedUserName = "";
    await loadSetupAdmin();
  } catch (error) {
    $("userSetupMessage").textContent = readableError(error);
    $("userSetupMessage").className = "message error";
  }
}

async function resetUserPassword(userName) {
  if (!userName) {
    $("userSetupMessage").textContent = "Select a user to reset.";
    $("userSetupMessage").className = "message error";
    return;
  }

  const temporaryPassword = window.prompt(`Enter a temporary password for ${userName}:`, "test");
  if (temporaryPassword === null) {
    return;
  }

  try {
    await api("/setup/users/reset-password", {
      method: "POST",
      body: JSON.stringify({ userName, temporaryPassword })
    });
    $("userSetupMessage").textContent = `${userName} password reset.`;
    $("userSetupMessage").className = "message ok";
    await loadSetupAdmin();
  } catch (error) {
    $("userSetupMessage").textContent = readableError(error);
    $("userSetupMessage").className = "message error";
  }
}

async function saveUser(event) {
  event.preventDefault();
  const userName = $("setupUserName").value.trim();
  try {
    await api("/setup/users", {
      method: "POST",
      body: JSON.stringify({
        userName,
        password: $("setupPassword").value,
        roles: [$("setupRole").value],
        productGroups: selectedUserProductGroups()
      })
    });
    $("setupPassword").value = "";
    $("userSetupMessage").textContent = "User saved.";
    $("userSetupMessage").className = "message ok";
    state.selectedUserName = userName;
    await loadSetupAdmin();
  } catch (error) {
    $("userSetupMessage").textContent = readableError(error);
    $("userSetupMessage").className = "message error";
  }
}

async function importUsersXlsx(event) {
  event.preventDefault();
  const file = $("userImportFile").files[0];
  if (!file) {
    $("userImportMessage").textContent = "Select a user permissions workbook to import.";
    $("userImportMessage").className = "message error";
    return;
  }

  try {
    const formData = new FormData();
    formData.append("file", file);
    const result = await api("/setup/users/import-xlsx", {
      method: "POST",
      body: formData
    });
    $("userImportMessage").textContent = `${result.count} users imported.`;
    $("userImportMessage").className = "message ok";
    $("userImportFile").value = "";
    await loadSetupAdmin();
  } catch (error) {
    $("userImportMessage").textContent = readableError(error);
    $("userImportMessage").className = "message error";
  }
}

function setupVariableRowTemplate() {
  return `
    <label class="setup-name-field"><span>Inspection item</span><input class="setup-characteristic-name" required></label>
    <label class="setup-type-field">
      <span>Type</span>
      <select class="setup-characteristic-type">
        <option value="Variable">Measured</option>
        <option value="Attribute">Accept / Reject</option>
      </select>
    </label>
    <label class="setup-unit-field"><span>Unit</span><input class="setup-unit" required></label>
    <label class="setup-location-field"><span>Requirement / context</span><input class="setup-location" placeholder="Front, Back, 2 places, if weld pool is present"></label>
    <label class="setup-method-field"><span>Method / tool</span><input class="setup-method" placeholder="Caliper, comparator FX194, template T-071"></label>
    <label class="setup-sample-field"><span>Sample size</span><input class="setup-row-sample-size" type="number" min="1" required></label>
    <label class="setup-frequency-type-field">
      <span>Frequency type</span>
      <select class="setup-row-frequency-type">
        <option value="Quantity">Quantity</option>
        <option value="Time">Time</option>
        <option value="Event">Event</option>
      </select>
    </label>
    <label class="setup-frequency-value-field"><span>Frequency</span><input class="setup-row-frequency-value" type="number" min="1" required></label>
    <label class="setup-frequency-unit-field">
      <span>Frequency unit</span>
      <select class="setup-row-frequency-unit"></select>
    </label>
    <label class="numeric-setup-field"><span>Target</span><input class="setup-nominal" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LSL</span><input class="setup-lsl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>USL</span><input class="setup-usl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LCL</span><input class="setup-lcl" type="number" step="0.0001"></label>
    <label class="numeric-setup-field"><span>UCL</span><input class="setup-ucl" type="number" step="0.0001"></label>
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

function setupMaterialRowTemplate() {
  return `
    <label><span>Material</span><input class="setup-material-name" required></label>
    <label><span>Material part number</span><input class="setup-material-part-num" required></label>
    <label><span>Description</span><input class="setup-material-description" required></label>
    <label>
      <span>Required</span>
      <select class="setup-material-required">
        <option value="true">Yes</option>
        <option value="false">No</option>
      </select>
    </label>
    <button type="button" class="secondary compact-button remove-material-button">Remove</button>`;
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

function addSetupMaterialRow(values = {}) {
  const row = document.createElement("div");
  row.className = "setup-material-row";
  row.dataset.originalMaterialName = values.materialName || "";
  row.innerHTML = setupMaterialRowTemplate();
  row.querySelector(".setup-material-name").value = values.materialName || "";
  row.querySelector(".setup-material-part-num").value = values.materialPartNum || "";
  row.querySelector(".setup-material-description").value = values.materialDescription || "";
  row.querySelector(".setup-material-required").value = String(values.isRequired ?? true);
  row.querySelector(".remove-material-button").addEventListener("click", () => row.remove());
  $("setupMaterialRows").appendChild(row);
}

function addSetupVariableRow(values = {}, type = values.characteristicType || "Variable") {
  const row = document.createElement("div");
  row.className = "setup-variable-row";
  row.dataset.originalCharacteristicName = values.characteristicName || "";
  row.innerHTML = setupVariableRowTemplate();
  row.querySelector(".setup-characteristic-name").value = values.characteristicName || "";
  row.querySelector(".setup-characteristic-type").value = type;
  row.querySelector(".setup-unit").value = values.unitOfMeasure || "";
  row.querySelector(".setup-location").value = values.location || "";
  row.querySelector(".setup-method").value = values.inspectionMethod || "";
  row.querySelector(".setup-row-sample-size").value = String(values.sampleSize || $("setupSampleSize").value || 1);
  row.querySelector(".setup-row-frequency-type").value = values.frequencyType || $("setupFrequencyType").value || "Quantity";
  updateRowFrequencyUnits(row, values.frequencyUnit || $("setupFrequencyUnit").value || "Pieces");
  row.querySelector(".setup-row-frequency-value").value = String(values.frequencyValue || $("setupFrequencyValue").value || 1);
  row.querySelector(".setup-nominal").value = values.nominal ?? "";
  row.querySelector(".setup-lsl").value = values.lsl ?? "";
  row.querySelector(".setup-usl").value = values.usl ?? "";
  row.querySelector(".setup-lcl").value = values.lcl ?? "";
  row.querySelector(".setup-ucl").value = values.ucl ?? "";
  row.querySelector(".setup-characteristic-type").addEventListener("change", () => updateSetupVariableType(row));
  row.querySelector(".setup-row-frequency-type").addEventListener("change", () => updateRowFrequencyUnits(row));
  row.querySelector(".remove-variable-button").addEventListener("click", () => {
    const container = row.parentElement;
    if (container?.children.length === 1 && container.id === "setupVariableRows") {
      row.querySelectorAll("input").forEach((input) => { input.value = ""; });
      row.querySelector(".setup-row-sample-size").value = String($("setupSampleSize").value || 1);
      row.querySelector(".setup-row-frequency-type").value = $("setupFrequencyType").value || "Quantity";
      updateRowFrequencyUnits(row, $("setupFrequencyUnit").value || "Pieces");
      row.querySelector(".setup-row-frequency-value").value = String($("setupFrequencyValue").value || 1);
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
  set.plans.forEach((plan) => addSetupVariableRow(plan, plan.characteristicType));
  $("setupJobDataFieldRows").innerHTML = "";
  (state.snapshot.partJobDataFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(firstPlan.inspectionPhase))
    .forEach((field) => addSetupJobDataFieldRow(field));
  $("setupMaterialRows").innerHTML = "";
  (state.snapshot.partMaterialFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(firstPlan.inspectionPhase))
    .forEach((field) => addSetupMaterialRow(field));
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
  $("setupJobDataFieldRows").innerHTML = "";
  $("setupMaterialRows").innerHTML = "";
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

function capabilityThresholdPayload() {
  return {
    yellowMinimum: Number($("capabilityYellowMinimum").value),
    greenMinimum: Number($("capabilityGreenMinimum").value)
  };
}

async function saveCapabilityThresholds(event) {
  event.preventDefault();
  try {
    const settings = await api("/setup/settings", {
      method: "POST",
      body: JSON.stringify({
        globalAlertRuleSet: $("globalAlertRuleSet").value,
        capabilityThresholds: capabilityThresholdPayload()
      })
    });
    state.snapshot.settings = settings;
    renderCapabilityThresholds();
    renderMeanSummary();
    renderPartReview();
    $("capabilityThresholdMessage").textContent = "Capability thresholds saved.";
    $("capabilityThresholdMessage").className = "message ok";
  } catch (error) {
    $("capabilityThresholdMessage").textContent = readableError(error);
    $("capabilityThresholdMessage").className = "message error";
  }
}

function updateRowFrequencyUnits(row, requestedUnit = null) {
  const unitsByType = {
    Quantity: [["Pieces", "Pieces"], ["Box", "Box"]],
    Time: [["Minutes", "Minutes"], ["Hours", "Hours"]],
    Event: [["StartOfJob", "Start of job"], ["MaterialChange", "Material change"], ["ToolChange", "Tool change"], ["Restart", "Restart"]]
  };
  const current = requestedUnit || row.querySelector(".setup-row-frequency-unit").value;
  const units = unitsByType[row.querySelector(".setup-row-frequency-type").value] || unitsByType.Quantity;
  fillSelect(row.querySelector(".setup-row-frequency-unit"), units, (unit) => unit[0], (unit) => unit[1]);
  if (units.some((unit) => unit[0] === current)) {
    row.querySelector(".setup-row-frequency-unit").value = current;
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
    Quantity: [["Pieces", "Pieces"], ["Box", "Box"]],
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
  return [...document.querySelectorAll(".setup-variable-row")].map((row, index) => ({
    originalCharacteristicName: row.dataset.originalCharacteristicName || null,
    characteristicName: row.querySelector(".setup-characteristic-name").value.trim(),
    characteristicType: row.querySelector(".setup-characteristic-type").value,
    unitOfMeasure: row.querySelector(".setup-unit").value.trim(),
    location: row.querySelector(".setup-location").value.trim(),
    inspectionMethod: row.querySelector(".setup-method").value.trim(),
    sampleSize: Number(row.querySelector(".setup-row-sample-size").value),
    frequencyType: row.querySelector(".setup-row-frequency-type").value,
    frequencyValue: Number(row.querySelector(".setup-row-frequency-value").value),
    frequencyUnit: row.querySelector(".setup-row-frequency-unit").value,
    nominal: Number(row.querySelector(".setup-nominal").value),
    lsl: Number(row.querySelector(".setup-lsl").value),
    usl: Number(row.querySelector(".setup-usl").value),
    lcl: optionalInputNumber(row.querySelector(".setup-lcl")),
    ucl: optionalInputNumber(row.querySelector(".setup-ucl")),
    isRequiredForCoa: false,
    coaStatisticType: "Mean",
    displayOrder: index + 1
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

function setupMaterialRows() {
  return [...document.querySelectorAll(".setup-material-row")]
    .map((row, index) => ({
      originalMaterialName: row.dataset.originalMaterialName || null,
      materialName: row.querySelector(".setup-material-name").value.trim(),
      materialPartNum: row.querySelector(".setup-material-part-num").value.trim(),
      materialDescription: row.querySelector(".setup-material-description").value.trim(),
      isRequired: row.querySelector(".setup-material-required").value === "true",
      displayOrder: index
    }))
    .filter((row) => row.materialName || row.materialPartNum || row.materialDescription);
}

function optionalInputNumber(input) {
  const value = input.value.trim();
  return value ? Number(value) : null;
}

async function saveInspectionSetup(event) {
  event.preventDefault();
  const variables = setupVariableRows();
  const jobDataFields = setupJobDataFieldRows();
  const materialFields = setupMaterialRows();
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

    for (const field of materialFields) {
      await api("/setup/material-fields", {
        method: "POST",
        body: JSON.stringify({
          partNum: baseRequest.partNum,
          inspectionPhase: baseRequest.inspectionPhase,
          materialName: field.materialName,
          materialPartNum: field.materialPartNum,
          materialDescription: field.materialDescription,
          isRequired: field.isRequired,
          displayOrder: field.displayOrder,
          originalMaterialName: field.originalMaterialName
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
          location: variable.location,
          inspectionMethod: variable.inspectionMethod,
          displayOrder: variable.displayOrder,
          sampleSize: variable.sampleSize,
          frequencyType: variable.frequencyType,
          frequencyValue: variable.frequencyValue,
          frequencyUnit: variable.frequencyUnit,
          isRequiredForCoa: variable.isRequiredForCoa,
          coaStatisticType: variable.coaStatisticType,
          originalProcessCode: state.editingSetup?.processCode || null,
          originalOperationSeq: state.editingSetup?.operationSeq || null,
          originalCharacteristicName: variable.originalCharacteristicName
        })
      });
    }

    $("inspectionSetupMessage").textContent = `${variables.length} inspection item${variables.length === 1 ? "" : "s"} saved for ${baseRequest.partNum}.`;
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

async function importXlsx(event) {
  event.preventDefault();
  const file = $("xlsxImportFile").files[0];
  if (!file) {
    $("csvImportMessage").textContent = "Select an Excel workbook to import.";
    $("csvImportMessage").className = "message error";
    return;
  }

  try {
    const formData = new FormData();
    formData.append("file", file);
    await api("/setup/import-xlsx", {
      method: "POST",
      body: formData
    });
    $("csvImportMessage").textContent = "Excel workbook imported.";
    $("csvImportMessage").className = "message ok";
    $("xlsxImportFile").value = "";
    await loadSnapshot();
  } catch (error) {
    $("csvImportMessage").textContent = readableError(error);
    $("csvImportMessage").className = "message error";
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
    "Part Number",
    "Part Description",
    "Product Group",
    "Inspection Phase",
    "Operation",
    "Job Data Field",
    "Material Name",
    "Material Part Number",
    "Material Description",
    "Variable Name",
    "Attribute Name",
    "Required",
    "Sort Order",
    "Unit",
    "Location",
    "Inspection Method",
    "Target",
    "Lower Spec",
    "Upper Spec",
    "Lower Control",
    "Upper Control",
    "Drift Rule",
    "COA Required",
    "COA Statistic",
    "Startup Required",
    "Startup Sample Size",
    "Startup Frequency Type",
    "Startup Frequency",
    "Startup Frequency Unit",
    "Setup Required",
    "Setup Sample Size",
    "Setup Frequency Type",
    "Setup Frequency",
    "Setup Frequency Unit",
    "In Process Required",
    "In Process Sample Size",
    "In Process Frequency Type",
    "In Process Frequency",
    "In Process Frequency Unit"
  ].join(",");
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
$("showChangePasswordButton").addEventListener("click", toggleChangePassword);
$("changePasswordButton").addEventListener("click", changePassword);
$("contextForm").addEventListener("submit", loadContext);
$("jobNum").addEventListener("input", () => {
  updatePartFromJob();
  clearWorkContext();
});
$("partNum").addEventListener("input", () => {
  refreshOperationChoices({ preserve: false });
  clearWorkContext();
});
$("operationCode").addEventListener("change", clearWorkContext);
$("inspectionPhase").addEventListener("change", clearWorkContext);
$("resourceId").addEventListener("change", clearWorkContext);
$("logoutButton").addEventListener("click", logout);
$("measurementForm").addEventListener("submit", submitMeasurement);
$("jobTagsForm").addEventListener("submit", saveJobTags);
$("materialChangeForm").addEventListener("submit", saveMaterialChange);
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
$("userImportForm").addEventListener("submit", importUsersXlsx);
$("newUserButton").addEventListener("click", newUser);
$("resetSelectedUserPasswordButton").addEventListener("click", () => resetUserPassword(state.selectedUserName));
$("deleteSelectedUserButton").addEventListener("click", () => deleteUser(state.selectedUserName));
$("selectAllUserProductGroups").addEventListener("click", () => setUserProductGroupSelection(productGroups()));
$("clearUserProductGroups").addEventListener("click", () => setUserProductGroupSelection([]));
$("inspectionSetupForm").addEventListener("submit", saveInspectionSetup);
$("addSetupVariableButton").addEventListener("click", () => addSetupVariableRow());
$("addSetupJobDataFieldButton").addEventListener("click", () => addSetupJobDataFieldRow());
$("addSetupMaterialButton").addEventListener("click", () => addSetupMaterialRow());
$("clearInspectionSetupButton").addEventListener("click", clearInspectionSetupForm);
$("loadPartSetupButton").addEventListener("click", loadSelectedPartSetup);
$("setupFrequencyType").addEventListener("change", updateSetupFrequencyUnits);
$("setupAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalAlertRuleSet").addEventListener("change", updateRuleDescription);
$("globalRuleForm").addEventListener("submit", saveGlobalRule);
$("capabilityThresholdForm").addEventListener("submit", saveCapabilityThresholds);
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
$("xlsxImportForm").addEventListener("submit", importXlsx);
$("csvTemplateButton").addEventListener("click", loadCsvTemplate);
$("partReviewFilter").addEventListener("change", loadReview);
$("partReviewFilter").addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    loadReview();
  }
});
$("reviewLoadButton").addEventListener("click", loadReview);
$("reviewJobNum").addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    event.preventDefault();
    loadReview();
  }
});
$("reportPartNum").addEventListener("change", refreshReportOperationChoices);
$("reportJobNum").addEventListener("change", refreshReportOperationChoices);
$("jobSummaryForm").addEventListener("submit", loadJobSummary);
$("jobSummaryCsvButton").addEventListener("click", openJobSummaryCsv);
$("reportForm").addEventListener("submit", runReport);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");
clearInspectionSetupForm();

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}


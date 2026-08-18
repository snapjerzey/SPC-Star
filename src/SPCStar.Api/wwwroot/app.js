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
  selectedUserName: "",
  selectedResourceId: "",
  currentShift: "1st Half Days",
  setupSection: "Inspection",
  historyView: "Ledger",
  historyFilters: {
    partNum: "",
    jobNum: ""
  },
  inspectionDrafts: new Map(),
  preserveInspectionEntriesUntil: 0,
  device: {
    serialPort: null,
    serialReader: null,
    serialReading: false,
    buffer: "",
    pending: Promise.resolve()
  }
};

const $ = (id) => document.getElementById(id);
const INSPECTION_PHASES = ["Startup", "Setup", "In Process", "Coil Change", "Spool"];
const MAX_LOT_NUMBER_LENGTH = 20;
const MAX_MEASUREMENT_DECIMAL_PLACES = 5;
const SESSION_STORAGE_KEY = "spc-star-session";
const WORK_CONTEXT_STORAGE_KEY = "spc-star-work-context";
const INSPECTION_DRAFT_STORAGE_KEY = "spc-star-inspection-drafts";

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
    const part = findPart(plan.partNum);
    const key = `${plan.partNum}|${plan.processCode}|${plan.operationSeq}|${plan.inspectionPhase || "In Process"}`;
    if (!map.has(key)) {
      map.set(key, {
        key,
        partNum: plan.partNum,
        partDescription: plan.partDescription,
        productGroup: plan.productGroup || "General",
        blankCode: part?.blankCode || "",
        holeSize: part?.holeSize || "",
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

function setupInspectionSets() {
  const map = new Map();
  state.snapshot.inspectionPlans.forEach((plan) => {
    const part = findPart(plan.partNum);
    const key = `${plan.partNum}|${plan.processCode}|${plan.operationSeq}`;
    if (!map.has(key)) {
      map.set(key, {
        key,
        partNum: plan.partNum,
        partDescription: plan.partDescription,
        productGroup: plan.productGroup || "General",
        blankCode: part?.blankCode || "",
        holeSize: part?.holeSize || "",
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

function findPart(partNum) {
  return (state.snapshot?.parts || []).find((part) => part.partNum.toLowerCase() === String(partNum || "").toLowerCase()) || null;
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

function requiredPhasesForOperation(set) {
  const partNum = String(set?.partNum || "").toLowerCase();
  const operationKey = operationKeyFor(set || {});
  const phases = new Set((state.snapshot?.inspectionPlans || [])
    .filter((plan) =>
      plan.partNum.toLowerCase() === partNum &&
      operationKeyFor(plan) === operationKey)
    .map((plan) => normalizeInspectionPhase(plan.inspectionPhase)));
  return {
    setup: phases.has("Setup"),
    startup: phases.has("Startup")
  };
}

async function requiredPhaseGate(jobNum, resourceId, set) {
  const phase = normalizeInspectionPhase(set?.activePhase || set?.inspectionPhase || $("inspectionPhase").value);
  if (!["Startup", "In Process"].includes(phase)) {
    return { allowed: true };
  }

  const required = requiredPhasesForOperation(set);
  if ((phase === "Startup" && !required.setup) || (phase === "In Process" && !required.setup && !required.startup)) {
    return { allowed: true };
  }

  const history = await jobHistoryForPhaseGate(jobNum);
  const hasSetup = !required.setup || phaseCompletionExists(history, set, resourceId, "Setup");
  const hasStartup = !required.startup || phaseCompletionExists(history, set, resourceId, "Startup");
  if (phase === "Startup" && !hasSetup) {
    return {
      allowed: false,
      message: `Setup is required before Startup for ${set.partNum} / ${operationLabelFor(set)} on ${resourceId}. Complete Setup first, then run Startup.`
    };
  }

  if (phase === "In Process") {
    if (!hasSetup) {
      return {
        allowed: false,
        message: `Setup is required before In Process for ${set.partNum} / ${operationLabelFor(set)} on ${resourceId}. Complete Setup first.`
      };
    }

    if (!hasStartup) {
      return {
        allowed: false,
        message: `Startup is required before In Process for ${set.partNum} / ${operationLabelFor(set)} on ${resourceId}. Complete Startup first.`
      };
    }
  }

  return { allowed: true };
}

async function jobHistoryForPhaseGate(jobNum) {
  if (!jobNum) {
    return [];
  }

  return api(`/jobs/${encodeURIComponent(jobNum)}/history`);
}

function phaseCompletionExists(history, set, resourceId, phase) {
  return (history || []).some((entry) =>
    entry.entryType === "PhaseComplete" &&
    String(entry.partNum || "").toLowerCase() === String(set.partNum || "").toLowerCase() &&
    String(entry.resourceId || "").toLowerCase() === String(resourceId || "").toLowerCase() &&
    String(entry.processCode || "").toLowerCase() === String(set.processCode || "").toLowerCase() &&
    Number(entry.operationSeq || 0) === Number(set.operationSeq || 0) &&
    normalizeInspectionPhase(entry.inspectionPhase) === normalizeInspectionPhase(phase));
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
  const signInButton = $("loginForm").querySelector("button[type='submit']");
  try {
    signInButton.disabled = true;
    $("loginMessage").textContent = "Signing in...";
    $("loginMessage").className = "message";
    const userName = $("userName").value.trim();
    const password = $("password").value;
    state.currentShift = $("loginShift").value;
    state.user = await api("/auth/login", {
      method: "POST",
      body: JSON.stringify({ userName, password })
    });
    await startAuthenticatedSession({ persist: true });
    $("loginMessage").textContent = "";
  } catch (error) {
    $("loginMessage").textContent = readableError(error);
    $("loginMessage").className = "message error";
    document.body.classList.add("login-active");
    $("loginPanel").classList.remove("hidden");
    $("workPanel").classList.add("hidden");
  } finally {
    signInButton.disabled = false;
  }
}

async function restoreAuthenticatedSession() {
  const saved = readSavedSession();
  if (!saved?.userName) {
    clearLoginFields();
    return;
  }

  try {
    $("loginMessage").textContent = "Restoring session...";
    $("loginMessage").className = "message";
    state.currentShift = saved.shift || $("loginShift").value;
    $("loginShift").value = state.currentShift;
    state.user = await api(`/auth/me?userName=${encodeURIComponent(saved.userName)}`);
    await startAuthenticatedSession({ persist: true });
    $("loginMessage").textContent = "";
  } catch {
    clearSavedSession();
    clearLoginFields();
    $("loginMessage").textContent = "Session expired. Sign in again.";
    $("loginMessage").className = "message";
  }
}

async function startAuthenticatedSession(options = {}) {
  $("loginMessage").textContent = "Loading inspection data...";
  setStatus($("userBadge"), `${state.user.userName} (${state.user.roles.join(", ")}) / ${state.currentShift}`, "ok");
  document.body.classList.remove("login-active");
  $("logoutButton").classList.remove("hidden");
  $("loginPanel").classList.add("hidden");
  $("workPanel").classList.remove("hidden");
  state.inspectionDrafts = readSavedInspectionDrafts();
  if (options.persist) {
    saveCurrentSession();
  }
  if (canAccessSetup()) {
    $("loginMessage").textContent = "Loading setup data...";
    $("navTabs").classList.remove("hidden");
    await loadSetupAdmin();
  }
  await loadSnapshot({ restoreWorkContext: true });
}

function saveCurrentSession() {
  window.localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify({
    userName: state.user?.userName || "",
    shift: state.currentShift || $("loginShift").value
  }));
}

function readSavedSession() {
  try {
    return JSON.parse(window.localStorage.getItem(SESSION_STORAGE_KEY) || "null");
  } catch {
    return null;
  }
}

function clearSavedSession() {
  window.localStorage.removeItem(SESSION_STORAGE_KEY);
}

function saveCurrentWorkContext() {
  if (!state.user?.userName) {
    return;
  }

  window.localStorage.setItem(WORK_CONTEXT_STORAGE_KEY, JSON.stringify({
    userName: state.user.userName,
    jobNum: $("jobNum").value.trim(),
    resourceId: $("resourceId").value,
    partNum: $("partNum").value.trim(),
    operationCode: $("operationCode").value,
    inspectionPhase: $("inspectionPhase").value
  }));
}

function readSavedWorkContext() {
  try {
    const saved = JSON.parse(window.localStorage.getItem(WORK_CONTEXT_STORAGE_KEY) || "null");
    if (!saved || saved.userName?.toLowerCase() !== state.user?.userName?.toLowerCase()) {
      return null;
    }
    return saved;
  } catch {
    return null;
  }
}

function clearSavedWorkContext() {
  window.localStorage.removeItem(WORK_CONTEXT_STORAGE_KEY);
}

function saveInspectionDrafts() {
  if (!state.user?.userName) {
    return;
  }

  window.localStorage.setItem(INSPECTION_DRAFT_STORAGE_KEY, JSON.stringify({
    userName: state.user.userName,
    entries: [...state.inspectionDrafts.entries()]
  }));
}

function readSavedInspectionDrafts() {
  try {
    const saved = JSON.parse(window.localStorage.getItem(INSPECTION_DRAFT_STORAGE_KEY) || "null");
    if (!saved || saved.userName?.toLowerCase() !== state.user?.userName?.toLowerCase() || !Array.isArray(saved.entries)) {
      return new Map();
    }
    return new Map(saved.entries);
  } catch {
    return new Map();
  }
}

function clearSavedInspectionDrafts() {
  window.localStorage.removeItem(INSPECTION_DRAFT_STORAGE_KEY);
}

function clearLoginFields() {
  $("userName").value = "";
  $("password").value = "";
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

async function loadSnapshot(options = {}) {
  state.snapshot = await api("/sync/setup-snapshot");
  fillDatalist($("jobOptions"), state.snapshot.jobs, (job) => job.jobNum);
  $("jobNum").value = "";
  fillSelect($("resourceId"), [{ resourceId: "", description: "Select machine" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillDatalist($("partOptions"), state.snapshot.parts, (part) => part.partNum);
  fillDatalist($("productGroupOptions"), productGroups(), (group) => group);
  $("partNum").value = "";
  refreshOperationChoices({ preserve: false });
  if (canAccessSetup()) {
    configureSetupAccess();
    renderPartReviewControls();
    renderReportControls();
    renderPartReview();
    if (canManageRules()) {
      renderGlobalRuleSetting();
    }
    if (canManageInspectionSetup()) {
      renderSetupEditChoices();
    }
    if (canManageUsers()) {
      renderUserProductGroupPicker();
    }
    if (canManageMachines()) {
      renderMachines();
    }
  }
  clearWorkContext();
  if (options.restoreWorkContext) {
    await restoreWorkContext();
  }
}

async function restoreWorkContext() {
  const saved = readSavedWorkContext();
  if (!saved?.jobNum || !saved.resourceId || !saved.partNum || !saved.operationCode) {
    return;
  }

  $("jobNum").value = saved.jobNum;
  $("partNum").value = saved.partNum;
  $("resourceId").value = state.snapshot.resources.some((resource) => resource.resourceId === saved.resourceId)
    ? saved.resourceId
    : "";
  $("inspectionPhase").value = saved.inspectionPhase || "In Process";
  refreshOperationChoices({ preserve: false });
  $("operationCode").value = [...$("operationCode").options].some((option) => option.value === saved.operationCode)
    ? saved.operationCode
    : "";

  if (!$("resourceId").value || !$("operationCode").value) {
    clearSavedWorkContext();
    return;
  }

  await loadContext();
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
  if (phase === "in process" || phase === "inprocess") return "In Process";
  return value.trim();
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
    renderEmptyContext(`Part ${partNum} is not set up. Ask QA or GOD to add the inspection plan before inspecting.`);
    return;
  }

  if (!set.plans.length) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext(`No inspection items are required for ${$("inspectionPhase").value} on ${partNum} / ${operationLabelFor(set)}.`);
    return;
  }

  const phaseGate = await requiredPhaseGate(jobNum, resourceId, set);
  if (!phaseGate.allowed) {
    state.selectedPlans = [];
    state.contexts = [];
    renderEmptyContext(phaseGate.message);
    return;
  }

  state.selectedPlans = set.plans;
  state.contexts = await Promise.all(set.plans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  saveCurrentWorkContext();
  renderContext();
}

function renderEmptyContext(message = "") {
  $("contextTitle").textContent = "Inspection Items";
  $("contextSubtitle").textContent = "Enter a job number, machine, part number, and operation, then start inspecting.";
  renderLock(null);
  $("measurementForm").classList.add("hidden");
  $("devicePanel").classList.add("hidden");
  $("trendPanel").classList.add("hidden");
  $("jobNotesPanel").classList.add("hidden");
  $("materialPanel").classList.add("hidden");
  $("tagsDivider").classList.add("hidden");
  $("tagsSection").classList.add("hidden");
  $("jobTagsList").innerHTML = "";
  $("measurementVariableList").innerHTML = "";
  $("machineCounter").value = "";
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
  renderDevicePanelForMachine(resourceId);
  $("measurementForm").classList.remove("hidden");
  $("trendPanel").classList.remove("hidden");
  $("jobNotesPanel").classList.remove("hidden");
  $("materialPanel").classList.remove("hidden");
  renderConfiguredJobDataFields(set);
  renderConfiguredMaterialFields(set);
  const hasEditableTags = document.querySelectorAll(".job-tag-input").length > 0;
  const hasPartFacts = document.querySelectorAll(".job-data-fact").length > 0;
  $("tagsDivider").classList.toggle("hidden", !hasEditableTags && !hasPartFacts);
  $("tagsSection").classList.toggle("hidden", !hasEditableTags && !hasPartFacts);
  $("jobTagsForm").classList.toggle("hidden", !hasEditableTags);
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
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(set.inspectionPhase) &&
      !isBuiltInOrPartStandardJobData(field.fieldName) &&
      !isMaterialLotJobDataField(field.fieldName))
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
  const form = $("jobTagsForm");
  $("jobTagsList").innerHTML = partStandardJobData(set).map((fact) => `
    <div class="job-data-fact">
      <span>${escapeHtml(fact.label)}</span>
      <strong>${escapeHtml(fact.value)}</strong>
    </div>`).join("");
  form.innerHTML = fields.map((field) => `
    <label>
      ${escapeHtml(field.fieldName)}
      <input class="job-tag-input" data-tag-name="${escapeHtml(field.fieldName)}" data-per-inspection="${isPerInspectionJobDataField(field.fieldName) ? "true" : "false"}" autocomplete="off" ${field.isRequired ? "required" : ""}>
    </label>`).join("") + (fields.length ? `<button type="submit" class="secondary">Save Job Data</button>` : "");
  form.querySelectorAll(".job-tag-input[data-per-inspection='true']").forEach((input) => {
    input.addEventListener("input", updateInspectionSubmitState);
  });
}

function isBuiltInOrPartStandardJobData(fieldName) {
  const normalized = String(fieldName || "").trim().toLowerCase();
  return normalized === "machine #" ||
    normalized === "machine number" ||
    normalized === "machine" ||
    normalized === "blank code" ||
    normalized === "hole size";
}

function isMaterialLotJobDataField(fieldName) {
  const normalized = String(fieldName || "").trim().toLowerCase();
  return normalized.includes("bimetal lot") ||
    normalized.includes("material lot") ||
    normalized.includes("raw material lot");
}

function isPerInspectionJobDataField(fieldName) {
  return isBoxNumberJobDataField(fieldName);
}

function partStandardJobData(set) {
  return [
    ["Blank Code", set.blankCode],
    ["Hole Size", set.holeSize]
  ]
    .filter(([, value]) => String(value || "").trim())
    .map(([label, value]) => ({ label, value }));
}

function renderConfiguredMaterialFields(set) {
  const fields = (state.snapshot.partMaterialFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase())
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
  const materialLotJobDataFields = (state.snapshot.partJobDataFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(set.inspectionPhase) &&
      isMaterialLotJobDataField(field.fieldName))
    .sort((a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0));
  const rows = $("materialFieldRows");
  const materialFields = fields.length || materialLotJobDataFields.length ? fields : [{
    materialName: "Material",
    materialPartNum: "",
    materialDescription: "",
    isRequired: true,
    displayOrder: 0
  }];
  const materialRows = materialFields.map((field, index) => `
    <section class="material-field-row">
      <h3>${escapeHtml(field.materialName)}${field.materialDescription ? ` - ${escapeHtml(field.materialDescription)}` : ""}</h3>
      <label>
        Material part number
        <input class="material-part-input" data-material-index="${index}" autocomplete="off" inputmode="text" value="${escapeHtml(field.materialPartNum || "")}" ${field.isRequired ? "required" : ""}>
      </label>
      <label>
        New lot number
        <input class="material-lot-input" data-material-index="${index}" autocomplete="off" inputmode="text" maxlength="${MAX_LOT_NUMBER_LENGTH}" ${field.isRequired ? "required" : ""}>
      </label>
      <label>
        Reason
        <select class="material-reason-input" data-material-index="${index}" required>
          <option value="Material change">Material change</option>
          <option value="Material issue at job start">Material issue at job start</option>
        </select>
      </label>
    </section>`);
  const materialLotRows = materialLotJobDataFields.map((field, index) => `
    <section class="material-field-row" data-material-tag-name="${escapeHtml(field.fieldName)}" data-material-name="${escapeHtml(materialNameFromLotField(field.fieldName))}">
      <h3>${escapeHtml(field.fieldName)}</h3>
      <label>
        New lot number
        <input class="material-lot-input" data-material-index="tag-${index}" autocomplete="off" inputmode="text" maxlength="${MAX_LOT_NUMBER_LENGTH}" ${field.isRequired ? "required" : ""}>
      </label>
      <label>
        Reason
        <select class="material-reason-input" data-material-index="tag-${index}" required>
          <option value="Material change">Material change</option>
          <option value="Material issue at job start">Material issue at job start</option>
        </select>
      </label>
    </section>`);
  rows.innerHTML = [...materialRows, ...materialLotRows].join("");
}

function materialNameFromLotField(fieldName) {
  return String(fieldName || "Material")
    .replace(/#/g, "")
    .replace(/\blot\b/ig, "")
    .replace(/\s+/g, " ")
    .trim() || "Material";
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
  updateGodReasonVisibility();
}

function overrideUserHasGodRole() {
  const userName = $("overrideUserName").value.trim();
  if (!userName) {
    return false;
  }

  if (state.user?.userName?.toLowerCase() === userName.toLowerCase() && userHasGodAccess(state.user)) {
    return true;
  }

  return (state.users || []).some((user) =>
    user.userName.toLowerCase() === userName.toLowerCase() &&
    userHasGodAccess(user));
}

function userHasGodAccess(user) {
  return (user?.roles || []).some((role) => role.toLowerCase() === "god") ||
    (user?.permissions || []).some((permission) => permission === "CanUseGodMode");
}

function updateGodReasonVisibility() {
  const isGodOverride = overrideUserHasGodRole();
  $("godReasonLabel").classList.toggle("hidden", !isGodOverride);
  $("causeCategoryLabel").classList.toggle("hidden", isGodOverride);
  $("causeTextLabel").classList.toggle("hidden", isGodOverride);
  $("solutionTextLabel").classList.toggle("hidden", isGodOverride);
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
        ${plan.inspectionMethod ? `
          <div class="inspection-item-context">
            <span class="inspection-tool">${escapeHtml(plan.inspectionMethod)}</span>
          </div>` : ""}
        ${isAttribute || isRecordOnly ? "" : `
          <div class="limit-grid">
            <div><span>LSL</span><strong>${formatNumber(context.lowerSpecLimit)}</strong></div>
            <div><span>Target</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>USL</span><strong>${formatNumber(context.upperSpecLimit)}</strong></div>
            <div><span>LCL</span><strong>${formatNumber(context.lowerControlLimit)}</strong></div>
            <div><span>Center</span><strong>${formatNumber(plan.nominal)}</strong></div>
            <div><span>UCL</span><strong>${formatNumber(context.upperControlLimit)}</strong></div>
          </div>`}
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
  document.querySelectorAll(".measurement-input").forEach((input) => {
    restoreMeasurementDraft(input);
  });
  $("machineCounter").value = "";
  wireMeasurementDeviceInputs();
  updateInspectionSubmitState();
}

function draftKeyForInput(input) {
  const plan = state.selectedPlans[Number(input.dataset.planIndex)];
  if (!plan) {
    return "";
  }

  const { jobNum, resourceId } = selectedValues();
  return [
    jobNum,
    resourceId,
    plan.partNum,
    plan.processCode,
    plan.operationSeq,
    plan.inspectionPhase || $("inspectionPhase").value,
    plan.characteristicName,
    input.dataset.sampleIndex
  ].join("|").toLowerCase();
}

function restoreMeasurementDraft(input) {
  const key = draftKeyForInput(input);
  const draft = key ? state.inspectionDrafts.get(key) : null;
  input.dataset.clientRecordId = draft?.clientRecordId || newClientRecordId();
  if (!draft) {
    return;
  }

  input.value = draft.value || "";
  input.dataset.submitted = draft.submitted ? "true" : "false";
  input.dataset.lastSubmittedValue = draft.lastSubmittedValue || "";
  input.dataset.lastSubmittedNumericValue = draft.lastSubmittedNumericValue || "";
}

function updateMeasurementDraft(input, updates = {}) {
  const key = draftKeyForInput(input);
  if (!key) {
    return;
  }

  const existing = state.inspectionDrafts.get(key) || {};
  state.inspectionDrafts.set(key, {
    ...existing,
    clientRecordId: input.dataset.clientRecordId || existing.clientRecordId || newClientRecordId(),
    value: input.value,
    submitted: input.dataset.submitted === "true",
    lastSubmittedValue: input.dataset.lastSubmittedValue || "",
    lastSubmittedNumericValue: input.dataset.lastSubmittedNumericValue || "",
    ...updates
  });
  saveInspectionDrafts();
}

function clearMeasurementDraftsForCurrentInspection() {
  const keys = [...document.querySelectorAll(".measurement-input:not(:disabled)")]
    .map((input) => draftKeyForInput(input))
    .filter(Boolean);
  keys.forEach((key) => state.inspectionDrafts.delete(key));
  saveInspectionDrafts();
}

function clearVisibleMeasurementInputs() {
  document.querySelectorAll(".measurement-input:not(:disabled)").forEach((input) => {
    input.value = "";
    input.dataset.clientRecordId = newClientRecordId();
    input.dataset.submitted = "false";
    input.dataset.lastSubmittedValue = "";
    input.dataset.lastSubmittedNumericValue = "";
  });
}

function snapshotMeasurementInputs() {
  return [...document.querySelectorAll(".measurement-input:not(:disabled)")].map((input) => ({
    key: draftKeyForInput(input),
    value: input.value,
    clientRecordId: input.dataset.clientRecordId || "",
    submitted: input.dataset.submitted === "true",
    lastSubmittedValue: input.dataset.lastSubmittedValue || "",
    lastSubmittedNumericValue: input.dataset.lastSubmittedNumericValue || ""
  })).filter((item) => item.key);
}

function restoreMeasurementInputSnapshot(snapshot) {
  if (!snapshot.length) {
    return;
  }

  const inputs = [...document.querySelectorAll(".measurement-input:not(:disabled)")];
  snapshot.forEach((item) => {
    state.inspectionDrafts.set(item.key, {
      clientRecordId: item.clientRecordId || newClientRecordId(),
      value: item.value,
      submitted: item.submitted,
      lastSubmittedValue: item.lastSubmittedValue,
      lastSubmittedNumericValue: item.lastSubmittedNumericValue
    });

    const input = inputs.find((candidate) => draftKeyForInput(candidate) === item.key);
    if (!input) {
      return;
    }

    input.value = item.value;
    input.dataset.clientRecordId = item.clientRecordId || input.dataset.clientRecordId || newClientRecordId();
    input.dataset.submitted = item.submitted ? "true" : "false";
    input.dataset.lastSubmittedValue = item.lastSubmittedValue;
    input.dataset.lastSubmittedNumericValue = item.lastSubmittedNumericValue;
  });
  saveInspectionDrafts();
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
  if (activeInput && activeInput.value !== activeInput.dataset.lastSubmittedValue) {
    const result = await submitSingleMeasurementAndAdvance(activeInput, false);
    if (result === "locked" || result === "error" || result === "empty") {
      return;
    }
  }

  if (!inspectionEntryComplete()) {
    showEntryMessage("Complete and save every sample before submitting the inspection.", "error");
    updateInspectionSubmitState();
    return;
  }

  await resetCompletedInspectionEntry();
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

  if (input.dataset.submitted === "true" && input.value === input.dataset.lastSubmittedValue) {
    if (moveNext) {
      focusNextMeasurementInput(input);
    }
    return "unchanged";
  }

  try {
    const result = await submitMeasurementInput(input, { reloadOnSuccess: false });
    if (moveNext && result !== "locked" && !state.activeLock) {
      focusNextMeasurementInput(input);
    }
    return result;
  } catch {
    return "error";
  }
}

function wireMeasurementDeviceInputs() {
  document.querySelectorAll(".measurement-input").forEach((input) => {
    input.addEventListener("focus", () => input.closest("label")?.classList.add("device-input-active"));
    input.addEventListener("input", () => {
      if (input.dataset.entryType === "Variable") {
        const limitedValue = capMeasurementDecimalPlaces(input.value);
        if (limitedValue !== input.value) {
          input.value = limitedValue;
        }
      }
      if (input.value !== input.dataset.lastSubmittedValue) {
        input.dataset.submitted = "false";
      }
      updateMeasurementDraft(input);
      updateInspectionSubmitState();
    });
    input.addEventListener("blur", async () => {
      input.closest("label")?.classList.remove("device-input-active");
      if (input.dataset.tabSubmitting === "true" || input.disabled || input.value === input.dataset.lastSubmittedValue) {
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
      window.setTimeout(() => {
        normalizeMeasurementInput(input);
        updateMeasurementDraft(input);
      }, 0);
    });
  });
}

async function submitMeasurementInput(input, options = {}) {
  if (input.disabled || input.dataset.submitting === "true") return;
  if (!inputHasValue(input)) return;
  const { jobNum, resourceId } = selectedValues();
  const plan = state.selectedPlans[Number(input.dataset.planIndex)];
  const phaseGate = await requiredPhaseGate(jobNum, resourceId, selectedInspectionSet());
  if (!phaseGate.allowed) {
    showEntryMessage(phaseGate.message, "error");
    throw new Error(phaseGate.message);
  }
  if (input.dataset.entryType === "Variable") {
    input.value = capMeasurementDecimalPlaces(input.value);
    updateMeasurementDraft(input);
  }

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
        clientRecordId: input.dataset.clientRecordId || newClientRecordId(),
        submittedAt: new Date().toISOString()
      })
    });
    markAcceptedMeasurementInput(input, value);
    showEntryMessage(`${sampleLabel(input)} saved.`, "ok");
    const planIndex = Number(input.dataset.planIndex);
    state.contexts[planIndex] = await loadVariableContext(jobNum, resourceId, plan);
    renderMeanSummary();
    if (state.contexts[planIndex]?.activeLock) {
      await loadJobNotes(jobNum);
      state.activeLock = state.contexts[planIndex].activeLock;
      renderLock(state.activeLock);
      showEntryMessage(`${sampleLabel(input)} saved. Lock detected.`, "error");
      return "locked";
    }
    if (inspectionEntryComplete()) {
      updateInspectionSubmitState();
      showEntryMessage("Inspection entries saved. Review them, then click Submit Inspection.", "ok");
      return "complete";
    }
    await loadJobNotes(jobNum);
    updateInspectionSubmitState();
    if (options.reloadOnSuccess === true) {
      await loadContext();
    }
    return "submitted";
  } catch (error) {
    showEntryMessage("Measurement rejected. " + readableError(error), "error");
    throw error;
  } finally {
    input.dataset.submitting = "false";
  }
}

function markAcceptedMeasurementInput(input, value) {
  input.dataset.submitted = "true";
  input.dataset.lastSubmittedValue = input.value;
  input.dataset.lastSubmittedNumericValue = String(value);
  updateMeasurementDraft(input, {
    value: input.value,
    submitted: true,
    lastSubmittedValue: input.value,
    lastSubmittedNumericValue: String(value)
  });
  const label = input.closest("label");
  label?.classList.add("measurement-submitted");
  window.setTimeout(() => label?.classList.remove("measurement-submitted"), 900);
  updateInspectionSubmitState();
}

function capMeasurementDecimalPlaces(value) {
  const normalized = String(value || "").replace(",", ".");
  const decimalIndex = normalized.indexOf(".");
  if (decimalIndex < 0) {
    return normalized;
  }

  const allowedEnd = decimalIndex + 1 + MAX_MEASUREMENT_DECIMAL_PLACES;
  return normalized.length > allowedEnd ? normalized.slice(0, allowedEnd) : normalized;
}

function inspectionEntryComplete() {
  const inputs = [...document.querySelectorAll(".measurement-input:not(:disabled)")];
  return inputs.length > 0 && inputs.every((input) =>
    input.dataset.submitted === "true" &&
    input.value === input.dataset.lastSubmittedValue);
}

function machineCounterValue() {
  return $("machineCounter").value.trim();
}

function machineCounterComplete() {
  const value = machineCounterValue();
  return value.length > 0 && Number.isInteger(Number(value)) && Number(value) >= 0;
}

function perInspectionJobDataComplete() {
  return [...document.querySelectorAll(".job-tag-input[data-per-inspection='true'][required]")]
    .every((input) => input.value.trim().length > 0);
}

function normalizeMachineCounterInput() {
  const input = $("machineCounter");
  input.value = input.value.replace(/[^\d]/g, "");
}

function updateInspectionSubmitState() {
  const button = $("completeInspectionButton");
  if (!button) {
    return;
  }

  const inputs = [...document.querySelectorAll(".measurement-input:not(:disabled)")];
  const hasInputs = inputs.length > 0;
  const isComplete = inspectionEntryComplete();
  button.classList.toggle("hidden", !hasInputs);
  $("machineCounter").disabled = !hasInputs;
  button.disabled = !isComplete || !machineCounterComplete() || !perInspectionJobDataComplete() || Boolean(state.activeLock);
}

async function resetCompletedInspectionEntry() {
  if (state.activeLock) {
    showEntryMessage("Clear the active lock before submitting this inspection.", "error");
    return;
  }

  if (!machineCounterComplete()) {
    showEntryMessage("Enter the Machine Counter before submitting this inspection.", "error");
    $("machineCounter").focus();
    return;
  }

  if (!perInspectionJobDataComplete()) {
    showEntryMessage("Complete the required per-inspection job data before submitting this inspection.", "error");
    document.querySelector(".job-tag-input[data-per-inspection='true'][required]:invalid, .job-tag-input[data-per-inspection='true'][required]")?.focus();
    return;
  }

  try {
    await saveCompletedInspection();
  } catch (error) {
    showEntryMessage("Inspection could not be submitted. " + readableError(error), "error");
    return;
  }

  state.preserveInspectionEntriesUntil = 0;
  clearMeasurementDraftsForCurrentInspection();
  clearVisibleMeasurementInputs();
  clearPerInspectionJobDataInputs();
  $("machineCounter").value = "";
  await loadContext();
  updateInspectionSubmitState();
  showEntryMessage("Inspection submitted. Fields cleared for the next inspection.", "ok");
}

async function saveCompletedInspection() {
  const { jobNum, resourceId, set } = selectedValues();
  if (!set) {
    throw new Error("No inspection plan is loaded.");
  }

  const phaseGate = await requiredPhaseGate(jobNum, resourceId, set);
  if (!phaseGate.allowed) {
    throw new Error(phaseGate.message);
  }

  await savePerInspectionJobDataForCompletion(jobNum, resourceId, set);

  await api("/inspections/complete", {
    method: "POST",
    body: JSON.stringify({
      jobNum,
      partNum: set.partNum,
      processCode: set.processCode,
      operationSeq: set.operationSeq,
      resourceId,
      inspectionPhase: set.activePhase || set.inspectionPhase || $("inspectionPhase").value,
      machineCounter: Number(machineCounterValue())
    })
  });
}

async function savePerInspectionJobDataForCompletion(jobNum, resourceId, set) {
  const tags = {};
  document.querySelectorAll(".job-tag-input[data-per-inspection='true']").forEach((input) => {
    const value = input.value.trim();
    if (value) {
      tags[input.dataset.tagName] = value;
    }
  });

  if (!Object.keys(tags).length) {
    return;
  }

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
}

function clearPerInspectionJobDataInputs() {
  document.querySelectorAll(".job-tag-input[data-per-inspection='true']").forEach((input) => {
    input.value = "";
  });
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
    updateMeasurementDraft(input);
  }
}

function parseDeviceMeasurement(rawValue) {
  const match = String(rawValue).replace(",", ".").match(/[-+]?\d*\.?\d+/);
  return match ? capMeasurementDecimalPlaces(match[0]) : null;
}

function setDeviceStatus(message, kind = "neutral") {
  $("deviceStatus").textContent = message;
  $("deviceStatus").className = `message ${kind}`;
}

function selectedMachineConfig(resourceId = $("resourceId").value) {
  return (state.snapshot?.resources || []).find((resource) => resource.resourceId.toLowerCase() === String(resourceId || "").toLowerCase()) || null;
}

function renderDevicePanelForMachine(resourceId) {
  const resource = selectedMachineConfig(resourceId);
  const isSerial = resource?.deviceProfile === "serial-text";
  $("devicePanel").classList.toggle("hidden", !isSerial);
  if (!isSerial) {
    setDeviceStatus("Keyboard input ready.", "neutral");
    return;
  }

  setDeviceStatus(`Gauge configured for ${resource.resourceId} at ${resource.serialBaudRate || 9600} baud.`, "neutral");
  updateDeviceControls(Boolean(state.device.serialPort));
}

function updateDeviceControls(connected) {
  $("connectSerialDeviceButton").classList.toggle("hidden", connected);
  $("disconnectSerialDeviceButton").classList.toggle("hidden", !connected);
}

async function connectSerialDevice() {
  if (!("serial" in navigator)) {
    const secureContextText = window.isSecureContext
      ? "Use desktop Chrome or Edge with Web Serial enabled."
      : "Serial gauges require a secure browser connection. Use HTTPS for the SPC-Star server URL, or test from localhost on the server.";
    setDeviceStatus(`Serial device connection is not available here. ${secureContextText}`, "error");
    return;
  }

  const resource = selectedMachineConfig();
  if (resource?.deviceProfile !== "serial-text") {
    setDeviceStatus("This machine is not configured for a serial gauge.", "error");
    return;
  }

  try {
    const port = await navigator.serial.requestPort();
    await port.open({ baudRate: Number(resource.serialBaudRate) || 9600 });
    state.device.serialPort = port;
    state.device.serialReading = true;
    state.device.buffer = "";
    updateDeviceControls(true);
    setDeviceStatus("Gauge connected.", "ok");
    readSerialDevice(port);
  } catch (error) {
    setDeviceStatus(readableError(error), "error");
    await disconnectSerialDevice();
  }
}

async function readSerialDevice(port) {
  const decoder = new TextDecoder();
  while (state.device.serialReading && port.readable) {
    const reader = port.readable.getReader();
    state.device.serialReader = reader;
    try {
      while (state.device.serialReading) {
        const { value, done } = await reader.read();
        if (done) break;
        if (value) {
          handleSerialDeviceText(decoder.decode(value, { stream: true }));
        }
      }
    } catch (error) {
      if (state.device.serialReading) {
        setDeviceStatus(readableError(error), "error");
      }
    } finally {
      reader.releaseLock();
      state.device.serialReader = null;
    }
  }
}

function handleSerialDeviceText(text) {
  state.device.buffer += text;
  const lines = state.device.buffer.split(/\r?\n/);
  state.device.buffer = lines.pop() || "";
  lines.map((line) => line.trim()).filter(Boolean).forEach(queueDeviceMeasurement);
  if (state.device.buffer.length > 80) {
    queueDeviceMeasurement(state.device.buffer);
    state.device.buffer = "";
  }
}

function queueDeviceMeasurement(rawValue) {
  state.device.pending = state.device.pending
    .catch(() => {})
    .then(() => applyDeviceMeasurement(rawValue));
}

async function applyDeviceMeasurement(rawValue) {
  const parsed = parseDeviceMeasurement(rawValue);
  if (parsed === null) {
    setDeviceStatus(`No numeric reading found in ${rawValue}.`, "error");
    return;
  }

  const input = activeMeasurementInput() || firstOpenMeasurementInput();
  if (!input) {
    setDeviceStatus("Load an inspection and select a measurement field.", "error");
    return;
  }

  if (input.dataset.entryType !== "Variable") {
    setDeviceStatus("Serial readings can only fill measured-variable fields.", "error");
    return;
  }

  input.focus();
  input.value = parsed;
  const result = await submitSingleMeasurementAndAdvance(input, true);
  if (result === "submitted") {
    setDeviceStatus(`Gauge reading ${parsed} accepted.`, "ok");
  } else if (result === "locked") {
    setDeviceStatus(`Gauge reading ${parsed} submitted and triggered a lock.`, "error");
  }
}

function activeMeasurementInput() {
  return document.activeElement?.classList?.contains("measurement-input") &&
    !document.activeElement.disabled &&
    document.activeElement.dataset.submitted !== "true"
    ? document.activeElement
    : null;
}

function firstOpenMeasurementInput() {
  return [...document.querySelectorAll(".measurement-input")]
    .find((input) => !input.disabled && input.dataset.submitted !== "true" && input.dataset.entryType === "Variable") || null;
}

async function disconnectSerialDevice() {
  state.device.serialReading = false;
  try {
    await state.device.serialReader?.cancel();
  } catch {
  }

  try {
    await state.device.serialPort?.close();
  } catch {
  }

  state.device.serialReader = null;
  state.device.serialPort = null;
  state.device.buffer = "";
  updateDeviceControls(false);
  renderDevicePanelForMachine($("resourceId").value);
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
      if (input && input.dataset.perInspection !== "true") {
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
    if (input.dataset.perInspection === "true") {
      return;
    }

    tags[input.dataset.tagName] = input.value.trim();
  });

  if (!Object.keys(tags).length) {
    $("tagMessage").textContent = "Per-inspection fields are saved when you submit the inspection.";
    $("tagMessage").className = "message";
    return;
  }

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
      materialPartNum: (row.querySelector(".material-part-input")?.value || row.dataset.materialName || "").trim(),
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

  if (entries.some((entry) => entry.newLotNum.length > MAX_LOT_NUMBER_LENGTH)) {
    $("materialMessage").textContent = `Lot number cannot exceed ${MAX_LOT_NUMBER_LENGTH} characters.`;
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
    item.className = `job-note-item ${entry.entryType === "Lock" ? "lock-history-item" : entry.entryType === "Material" ? "material-history-item" : entry.entryType === "JobData" ? "job-data-history-item" : entry.entryType === "PhaseComplete" ? "phase-complete-history-item" : ""}`;
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
    } else if (entry.entryType === "JobData") {
      text.textContent = jobDataHistoryText(entry);
    } else if (entry.entryType === "PhaseComplete") {
      text.textContent = phaseCompletionHistoryText(entry);
    } else if (entry.entryType === "MeasurementEdit") {
      text.textContent = measurementEditHistoryText(entry);
    } else if (entry.entryType === "Measurement") {
      text.textContent = measurementHistoryText(entry);
    } else {
      text.textContent = entry.noteText;
    }
    meta.append(user, details);
    item.append(meta, text);
    if (entry.entryType === "Material" && canEditMaterialLots()) {
      const editButton = document.createElement("button");
      editButton.type = "button";
      editButton.className = "secondary compact-history-button";
      editButton.textContent = "Edit Lot";
      editButton.addEventListener("click", () => editMaterialLot(entry));
      item.appendChild(editButton);
    }
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

  if (entry.entryType === "JobData") {
    if (isMaterialLotJobDataField(entry.tagName)) {
      return `Material - ${entry.tagName || "Lot"}`;
    }

    return `Job Data - ${entry.tagName || "Field"}`;
  }

  if (entry.entryType === "PhaseComplete") {
    return `${entry.inspectionPhase || "Inspection"} inspection ${entry.completionNumber || 1} completed`;
  }

  return entry.operatorUserId;
}

function historyEntryUser(entry) {
  const shift = entry.operatorShift ? ` (${entry.operatorShift})` : "";
  if (entry.entryType === "Lock" && entry.status !== "Active" && entry.overrideUserId) {
    return `${entry.overrideUserId}${shift}`;
  }

  return entry.operatorUserId ? `${entry.operatorUserId}${shift}` : "-";
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

function phaseCompletionHistoryText(entry) {
  const operation = entry.processCode
    ? ` Operation: ${entry.processCode}${entry.operationSeq ? ` ${entry.operationSeq}` : ""}.`
    : "";
  const counter = entry.machineCounter !== null && entry.machineCounter !== undefined
    ? ` Machine Counter: ${entry.machineCounter}.`
    : "";
  const box = inspectionBoxNumberText(entry);
  return `${entry.inspectionPhase || "Inspection"} inspection ${entry.completionNumber || 1} completed by ${historyEntryUser(entry)}.${counter}${box}${operation}`;
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

function jobDataHistoryText(entry) {
  if (isMaterialLotJobDataField(entry.tagName)) {
    return `${entry.tagName || "Material lot"}: ${entry.tagValue || "-"}. Recorded by ${entry.operatorUserId}.`;
  }

  return `${entry.tagName || "Job data"}: ${entry.tagValue || "-"}. Recorded by ${entry.operatorUserId}.`;
}

function inspectionBoxNumberText(entry) {
  const boxEntry = (entry.jobDataEntries || [])
    .find((jobData) => isBoxNumberJobDataField(jobData.tagName));
  return boxEntry ? ` ${boxEntry.tagName}: ${boxEntry.tagValue || "-"}.` : "";
}

function isBoxNumberJobDataField(fieldName) {
  const normalized = String(fieldName || "").trim().toLowerCase();
  return normalized === "box" ||
    normalized === "box #" ||
    normalized === "box number" ||
    normalized === "serial #" ||
    normalized === "serial number";
}

function canEditMaterialLots() {
  return hasPermission("CanManageInspectionPlans") || hasPermission("CanUseGodMode");
}

async function editMaterialLot(entry, options = {}) {
  const currentLot = entry.newLotNum || "";
  const newLot = window.prompt(`Correct lot number for ${entry.materialPartNum || "material"}:`, currentLot);
  if (newLot === null) {
    return;
  }

  const trimmedLot = newLot.trim();
  if (!trimmedLot) {
    showEntryMessage("Corrected lot number is required.", "error");
    return;
  }

  if (trimmedLot.length > MAX_LOT_NUMBER_LENGTH) {
    showEntryMessage(`Lot number cannot exceed ${MAX_LOT_NUMBER_LENGTH} characters.`, "error");
    return;
  }

  try {
    await api(`/material-changes/${entry.id}/lot`, {
      method: "PATCH",
      body: JSON.stringify({
        newLotNum: trimmedLot,
        editedByUserId: state.user.userName,
        editedAt: new Date().toISOString()
      })
    });
    const activeJobNum = selectedValues().jobNum || entry.jobNum;
    if (activeJobNum) {
      await loadJobNotes(activeJobNum);
    }
    if (options.refreshReview) {
      await loadReview();
    }
    showMaterialEditMessage("Material lot corrected.", "ok", options);
  } catch (error) {
    showMaterialEditMessage("Material lot was not updated. " + readableError(error), "error", options);
  }
}

function showMaterialEditMessage(message, kind, options = {}) {
  if (options.refreshReview && $("reviewMessage")) {
    $("reviewMessage").textContent = message;
    $("reviewMessage").className = `message ${kind}`;
    return;
  }

  showEntryMessage(message, kind);
}

async function clearLock(event) {
  event.preventDefault();
  if (!state.activeLock) {
    return;
  }

  const preservedInputs = snapshotMeasurementInputs();
  state.preserveInspectionEntriesUntil = Date.now() + 10000;
  try {
    const isGodOverride = overrideUserHasGodRole();
    await api(`/alerts/${state.activeLock.alertId}/override`, {
      method: "POST",
      body: JSON.stringify({
        overrideUserName: $("overrideUserName").value.trim(),
        overridePassword: $("overridePassword").value,
        causeCategory: isGodOverride ? "Unspecified" : $("causeCategory").value,
        causeText: isGodOverride ? "" : $("causeText").value.trim(),
        solutionText: isGodOverride ? "" : $("solutionText").value.trim(),
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
    state.activeLock = null;
    renderLock(null);
    await refreshContextDataWithoutClearingEntries();
    restoreMeasurementInputSnapshot(preservedInputs);
    window.setTimeout(() => restoreMeasurementInputSnapshot(preservedInputs), 0);
    window.setTimeout(() => restoreMeasurementInputSnapshot(preservedInputs), 300);
  } catch (error) {
    $("overrideMessage").textContent = readableError(error);
    $("overrideMessage").className = "message error";
  }
}

async function refreshContextDataWithoutClearingEntries() {
  const { jobNum, resourceId } = selectedValues();
  if (!jobNum || !resourceId || !state.selectedPlans.length) {
    return;
  }

  state.contexts = await Promise.all(state.selectedPlans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  state.activeLock = state.contexts.find((context) => context.activeLock)?.activeLock || null;
  renderLock(state.activeLock);
  renderMeanSummary();
  renderTrendChoices();
  await loadTrend();
  await loadJobNotes(jobNum);
}

async function refreshLockStatus() {
  $("overrideMessage").textContent = "Refreshing lock status...";
  $("overrideMessage").className = "message";
  const preservedInputs = snapshotMeasurementInputs();
  state.preserveInspectionEntriesUntil = Date.now() + 10000;
  try {
    await refreshContextDataWithoutClearingEntries();
    restoreMeasurementInputSnapshot(preservedInputs);
    if (state.activeLock) {
      $("overrideMessage").textContent = "Lock is still active. Use authorized credentials to clear or bypass it.";
      $("overrideMessage").className = "message error";
    } else {
      $("overrideMessage").textContent = "No active lock found. Inspection entry is available again.";
      $("overrideMessage").className = "message ok";
    }
  } catch (error) {
    $("overrideMessage").textContent = "Lock status was not refreshed. " + readableError(error);
    $("overrideMessage").className = "message error";
  }
}

function resetLockForm() {
  $("overrideUserName").value = canCurrentUserOverride() ? state.user.userName : "";
  $("overridePassword").value = "";
  $("causeCategory").value = "Machine";
  $("causeText").value = "";
  $("solutionText").value = "";
  $("bypassReason").value = "";
  $("overrideMessage").textContent = "Unlock form reset.";
  $("overrideMessage").className = "message";
  updateGodReasonVisibility();
}

function canCurrentUserOverride() {
  return state.user?.permissions?.includes("CanOverrideDriftLock") === true;
}

function hasPermission(permission) {
  return state.user?.permissions?.includes(permission) === true;
}

function canManageSetup() {
  return canManageInspectionSetup() || canManageMachines() || canManageUsers() || canManageRules() || canArchiveData();
}

function canManageInspectionSetup() {
  return hasPermission("CanManageInspectionPlans");
}

function canManageMachines() {
  return hasPermission("CanManageInspectionPlans");
}

function canManageUsers() {
  return hasPermission("CanManageUsers");
}

function canManageRules() {
  return hasPermission("CanManageInspectionPlans");
}

function canImportSetupData() {
  return hasPermission("CanImportSetupData");
}

function assignableRoles() {
  return canArchiveData()
    ? state.roles
    : state.roles.filter((role) => role.toLowerCase() !== "god");
}

function actingSessionQuery() {
  return `actingUserName=${encodeURIComponent(state.user?.userName || "")}&actingSessionToken=${encodeURIComponent(state.user?.sessionToken || "")}`;
}

function canArchiveData() {
  return hasPermission("CanUseGodMode");
}

function canViewHistory() {
  return hasPermission("CanExportQAData") || canManageSetup();
}

function canAccessSetup() {
  return canManageSetup() || canViewHistory();
}

function setupSectionAccess(sectionName) {
  return {
    Inspection: canManageInspectionSetup(),
    Machines: canManageMachines(),
    Users: canManageUsers(),
    Rules: canManageRules(),
    History: canViewHistory(),
    Archive: canArchiveData()
  }[sectionName] === true;
}

function defaultSetupSection() {
  return ["Inspection", "Machines", "Users", "Rules", "History", "Archive"].find(setupSectionAccess) || "History";
}

function setSetupSectionVisibility(sectionName) {
  const sections = ["Inspection", "Machines", "Users", "Rules", "History", "Archive"];
  sections.forEach((section) => {
    $(`setup${section}Section`).classList.toggle("hidden", section !== sectionName);
    $(`setup${section}SectionTab`).classList.toggle("active", section === sectionName);
  });
}

function configureSetupAccess() {
  ["Inspection", "Machines", "Users", "Rules", "History", "Archive"].forEach((section) => {
    const hasAccess = setupSectionAccess(section);
    $(`setup${section}SectionTab`).classList.toggle("hidden", !hasAccess);
    if (!hasAccess) {
      $(`setup${section}Section`).classList.add("hidden");
    }
  });
  if (!setupSectionAccess(state.setupSection)) {
    state.setupSection = defaultSetupSection();
  }
  setSetupSectionVisibility(state.setupSection);
  document.querySelectorAll(".import-only").forEach((element) => {
    element.classList.toggle("hidden", !canImportSetupData());
  });
}

function showPanel(panelName) {
  const showSetup = panelName === "setup";
  if (showSetup) {
    configureSetupAccess();
    showSetupSection(state.setupSection || defaultSetupSection());
  }
  $("workPanel").classList.toggle("hidden", showSetup);
  $("setupPanel").classList.toggle("hidden", !showSetup);
  $("inspectionTab").classList.toggle("active", !showSetup);
  $("setupTab").classList.toggle("active", showSetup);
}

function showSetupSection(sectionName) {
  if (!setupSectionAccess(sectionName)) {
    sectionName = defaultSetupSection();
  }
  state.setupSection = sectionName;

  if (sectionName === "History") {
    seedHistoryFiltersFromWorkContext();
    applyHistoryFilters();
  }

  setSetupSectionVisibility(sectionName);
}

function seedHistoryFiltersFromWorkContext() {
  if (!state.historyFilters.partNum) {
    state.historyFilters.partNum = $("partNum").value.trim();
  }

  if (!state.historyFilters.jobNum) {
    state.historyFilters.jobNum = $("jobNum").value.trim();
  }

  if (state.historyFilters.jobNum && !state.historyFilters.partNum) {
    state.historyFilters.partNum = partNumForJob(state.historyFilters.jobNum);
  }
}

function showHistoryView(viewName) {
  syncHistoryFiltersFrom(state.historyView);
  state.historyView = viewName;
  applyHistoryFilters();
  const views = ["Ledger", "Charts", "Issues", "Export"];
  views.forEach((view) => {
    $(`history${view}View`).classList.toggle("hidden", view !== viewName);
    $(`history${view}Tab`).classList.toggle("active", view === viewName);
  });
}

function syncHistoryFiltersFrom(source) {
  if (source === "Ledger") {
    state.historyFilters.partNum = $("partReviewFilter").value.trim();
    state.historyFilters.jobNum = $("reviewJobNum").value.trim();
  } else if (source === "Charts") {
    state.historyFilters.partNum = $("reportPartNum").value.trim();
    state.historyFilters.jobNum = $("reportJobNum").value.trim();
  } else if (source === "Issues") {
    state.historyFilters.partNum = $("topIssuesPartNum").value.trim();
    state.historyFilters.jobNum = $("topIssuesJobNum").value.trim();
  } else if (source === "Export") {
    const jobNum = firstHistoryJobNum($("summaryJobNum").value);
    if (jobNum) {
      state.historyFilters.jobNum = jobNum;
      state.historyFilters.partNum = partNumForJob(jobNum) || state.historyFilters.partNum;
    }
  }

  if (state.historyFilters.jobNum && !state.historyFilters.partNum) {
    state.historyFilters.partNum = partNumForJob(state.historyFilters.jobNum);
  }
}

function applyHistoryFilters() {
  const partNum = state.historyFilters.partNum || "";
  const jobNum = state.historyFilters.jobNum || "";
  $("partReviewFilter").value = partNum;
  $("reviewJobNum").value = jobNum;
  $("reportPartNum").value = partNum;
  $("reportJobNum").value = jobNum;
  $("topIssuesPartNum").value = partNum;
  $("topIssuesJobNum").value = jobNum;
  const summaryJobNum = $("summaryJobNum").value.trim();
  if (jobNum && (!summaryJobNum || !summaryJobNum.includes(","))) {
    $("summaryJobNum").value = jobNum;
  }
  refreshReportOperationChoices();
}

function firstHistoryJobNum(value) {
  return value.split(",").map((jobNum) => jobNum.trim()).filter(Boolean)[0] || "";
}

function partNumForJob(jobNum) {
  return state.snapshot?.jobs?.find((job) => job.jobNum.toLowerCase() === jobNum.toLowerCase())?.partNum || "";
}

function logout() {
  disconnectSerialDevice();
  clearSavedSession();
  clearSavedWorkContext();
  clearSavedInspectionDrafts();
  state.user = null;
  state.snapshot = null;
  state.contexts = [];
  state.selectedPlans = [];
  state.jobNotes = [];
  state.trendCharacteristic = "";
  state.activeLock = null;
  state.inspectionDrafts = new Map();
  state.users = [];
  state.roles = [];
  state.currentShift = $("loginShift").value;
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

function clearSelectedWorkContext() {
  clearSavedWorkContext();
  clearWorkContext();
}

async function loadSetupAdmin() {
  configureSetupAccess();
  if (canManageUsers()) {
    state.roles = await api("/setup/roles");
    state.users = await api("/setup/users");
    fillSelect($("setupRole"), assignableRoles(), (role) => role, (role) => role);
    renderUserProductGroupPicker();
    renderUsers();
  }
}

function renderPartReviewControls() {
  $("partReviewFilter").placeholder = "Part number";
}

function renderReportControls() {
  refreshReportOperationChoices();
  fillSelect($("reportResourceId"), [{ resourceId: "", description: "All machines" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  fillSelect($("topIssuesResourceId"), [{ resourceId: "", description: "All machines" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
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
  const sets = [{ key: "", label: "Create new part setup" }, ...setupInspectionSets().map((set) => ({
    key: set.key,
    label: `${set.partNum} / ${set.productGroup || "General"} / ${set.processCode}`
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
      <span>Scope</span><span>Operation</span><span>Phase</span><span>Variable</span><span>Type</span><span>Mean</span><span>Std Dev</span><span>Cpk</span><span>Ppk</span><span>Count</span>
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
      <span>${capabilityBadge(row.cpk)}</span>
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
  const measurementById = new Map((measurements || []).map((measurement) => [String(measurement.id).toLowerCase(), measurement]));
  const jobDataEntries = (history || []).filter((entry) => entry.entryType === "JobData");
  const materialEntries = (history || []).filter((entry) => entry.entryType === "Material");
  const phaseCompletions = (history || []).filter((entry) => entry.entryType === "PhaseComplete");
  const completedMeasurementIds = new Set(
    phaseCompletions
      .filter((entry) => Array.isArray(entry.measurementIds))
      .flatMap((entry) => entry.measurementIds.map((id) => String(id).toLowerCase()))
  );
  const uncompletedMeasurements = (measurements || [])
    .filter((measurement) => !completedMeasurementIds.has(String(measurement.id).toLowerCase()));
  const historyRows = (history || [])
    .filter((entry) => {
      if (entry.entryType === "JobData") {
        return !phaseCompletions.some((completion) => reviewJobDataBelongsToCompletion(entry, completion));
      }

      if (entry.entryType === "Material") {
        return !phaseCompletions.some((completion) => reviewTraceabilityEventBelongsToCompletion(entry, completion));
      }

      return true;
    });
  const rows = [
    ...groupReviewMeasurements(uncompletedMeasurements).map((group) => ({ kind: "MeasurementGroup", timestamp: group.latest.timestamp, group })),
    ...historyRows.map((entry) => ({ kind: "History", timestamp: entry.timestamp, entry }))
  ].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));

  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = "No job history for this job.";
    return;
  }

  container.className = "data-table review-measurement-table";
  container.innerHTML = `
    <div class="data-row header">
      <span>Latest</span><span>Phase</span><span>Inspection Item</span><span>Summary / Details</span><span>Machine</span><span>Operation</span><span>User</span><span></span>
    </div>`;
  rows.forEach((row) => {
    if (row.kind === "History") {
      renderReviewHistoryEvent(container, row.entry, measurementById, jobDataEntries, materialEntries);
      return;
    }

    renderReviewMeasurementGroup(container, row.group);
  });
}

function reviewJobDataBelongsToCompletion(jobDataEntry, completionEntry) {
  if (jobDataEntry.entryType !== "JobData" || completionEntry.entryType !== "PhaseComplete") {
    return false;
  }

  if (String(jobDataEntry.partNum || "").toLowerCase() !== String(completionEntry.partNum || "").toLowerCase()) {
    return false;
  }

  if (String(jobDataEntry.resourceId || "").toLowerCase() !== String(completionEntry.resourceId || "").toLowerCase()) {
    return false;
  }

  return new Date(jobDataEntry.timestamp) <= new Date(completionEntry.timestamp);
}

function reviewTraceabilityEventBelongsToCompletion(traceabilityEntry, completionEntry) {
  if (completionEntry.entryType !== "PhaseComplete") {
    return false;
  }

  if (String(traceabilityEntry.partNum || "").toLowerCase() !== String(completionEntry.partNum || "").toLowerCase()) {
    return false;
  }

  if (String(traceabilityEntry.resourceId || "").toLowerCase() !== String(completionEntry.resourceId || "").toLowerCase()) {
    return false;
  }

  const traceabilityTime = new Date(traceabilityEntry.timestamp);
  const completionTime = new Date(completionEntry.timestamp);
  if (Number.isNaN(traceabilityTime.getTime()) || Number.isNaN(completionTime.getTime())) {
    return false;
  }

  return traceabilityTime <= completionTime &&
    traceabilityTime >= new Date(completionTime.getTime() - 2 * 60 * 1000);
}

function groupReviewMeasurements(measurements) {
  const groups = new Map();
  measurements.forEach((measurement) => {
    const key = [
      measurement.processCode,
      measurement.operationSeq,
      measurement.resourceId,
      reviewMeasurementDateKey(measurement.timestamp),
      normalizeInspectionPhase(measurement.inspectionPhase),
      measurement.characteristicName,
      measurement.characteristicType
    ].join("|");
    if (!groups.has(key)) {
      groups.set(key, []);
    }
    groups.get(key).push(measurement);
  });

  return [...groups.values()].map((items, index) => {
    const sorted = [...items].sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));
    return {
      id: `review-measurement-group-${index}`,
      items: sorted,
      latest: sorted[0],
      summary: reviewMeasurementGroupSummary(sorted)
    };
  });
}

function reviewMeasurementGroupSummary(items) {
  const latest = items[0];
  const outOfSpec = items.filter((item) => item.isOutOfSpec).length;
  const outOfControl = items.filter((item) => item.isOutOfControl).length;
  if (latest.characteristicType === "Attribute") {
    const accepted = items.filter((item) => Number(item.value) === 1).length;
    const rejected = items.length - accepted;
    return `${items.length} entries · Accept ${accepted} · Reject ${rejected}${outOfSpec ? ` · ${outOfSpec} out of spec` : ""}`;
  }

  const values = items.map((item) => Number(item.value)).filter(Number.isFinite);
  const mean = values.reduce((total, value) => total + value, 0) / Math.max(values.length, 1);
  const min = values.length ? Math.min(...values) : null;
  const max = values.length ? Math.max(...values) : null;
  const flags = [
    outOfSpec ? `${outOfSpec} out of spec` : "",
    outOfControl ? `${outOfControl} out of control` : ""
  ].filter(Boolean).join(" · ");
  return `${items.length} entries · Mean ${formatNumber(mean)} · Range ${formatNumber(min)} - ${formatNumber(max)}${flags ? ` · ${flags}` : ""}`;
}

function reviewMeasurementDateKey(timestamp) {
  const date = new Date(timestamp);
  if (Number.isNaN(date.getTime())) {
    return "";
  }

  return date.toISOString().slice(0, 10);
}

function renderReviewMeasurementGroup(container, group) {
  const measurement = group.latest;
  const machines = [...new Set(group.items.map((item) => item.resourceId))];
  const users = [...new Set(group.items.map((item) => item.operatorShift ? `${item.operatorUserId} (${item.operatorShift})` : item.operatorUserId))];
  const item = document.createElement("div");
  item.className = `data-row review-measurement-group-row ${group.items.some((entry) => entry.isOutOfSpec) ? "measurement-out-spec" : group.items.some((entry) => entry.isOutOfControl) ? "measurement-out-control" : ""}`;
  item.innerHTML = `
    <span>${formatDateTime(measurement.timestamp)}</span>
    <span>${escapeHtml(measurement.inspectionPhase)}</span>
    <span>${escapeHtml(measurement.characteristicName)}<small>${escapeHtml(measurement.characteristicType === "Attribute" ? "Accept/Reject" : "Measured")}</small></span>
    <span>${escapeHtml(group.summary)}</span>
    <span>${escapeHtml(machines.join(", "))}</span>
    <span>${escapeHtml(measurement.processCode)} ${measurement.operationSeq}</span>
    <span>${escapeHtml(users.slice(0, 2).join(", "))}${users.length > 2 ? `<small>${users.length - 2} more</small>` : ""}</span>
    <span><button type="button" class="secondary compact-button">Details</button></span>`;
  item.querySelector("button").addEventListener("click", () => toggleReviewMeasurementGroup(container, group.id));
  container.appendChild(item);
  group.items.forEach((entry) => renderReviewMeasurementDetail(container, group.id, entry));
}

function toggleReviewMeasurementGroup(container, groupId) {
  const rows = container.querySelectorAll(`[data-review-group="${groupId}"]`);
  const shouldShow = [...rows].some((row) => row.classList.contains("hidden"));
  rows.forEach((row) => row.classList.toggle("hidden", !shouldShow));
}

function renderReviewMeasurementDetail(container, groupId, measurement) {
  const item = document.createElement("div");
  item.dataset.reviewGroup = groupId;
  item.className = `data-row review-measurement-detail-row hidden ${measurement.isOutOfSpec ? "measurement-out-spec" : measurement.isOutOfControl ? "measurement-out-control" : ""}`;
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
    <span>${escapeHtml(measurement.characteristicName)}</span>
    <span>${reviewMeasurementValueControl(measurement)}</span>
    <span>${escapeHtml(measurement.resourceId)}${measurement.isOutOfSpec ? ` <strong class="status-text bad">Out of spec</strong>` : measurement.isOutOfControl ? ` <strong class="status-text warn">Out of control</strong>` : ""}</span>
    <span>${escapeHtml(measurement.processCode)} ${measurement.operationSeq}</span>
    <span>${escapeHtml(measurement.operatorUserId)}${measurement.operatorShift ? ` (${escapeHtml(measurement.operatorShift)})` : ""}</span>
    <span><button type="button" class="secondary compact-button">Save</button></span>`;
  item.querySelector("button").addEventListener("click", () => saveReviewMeasurement(measurement.id, item));
  container.appendChild(item);
}

function renderReviewHistoryEvent(container, entry, measurementById = new Map(), jobDataEntries = [], materialEntries = []) {
  const item = document.createElement("div");
  item.className = `data-row review-history-event-row ${entry.entryType === "Lock" ? "measurement-out-control" : ""} ${entry.entryType === "MeasurementEdit" ? "measurement-edit-history" : ""} ${entry.entryType === "PhaseComplete" ? "phase-complete-history-row" : ""}`;
  const completionMeasurements = inspectionCompletionMeasurements(entry, measurementById);
  const completionJobData = inspectionCompletionJobData(entry, jobDataEntries);
  const completionMaterials = inspectionCompletionMaterials(entry, materialEntries);
  const completionMetadata = inspectionCompletionMetadata(entry);
  const completionGroupId = `review-inspection-completion-${entry.id}`;
  const details = entry.entryType === "Lock"
    ? lockHistoryText(entry)
    : entry.entryType === "Material"
      ? materialHistoryText(entry)
      : entry.entryType === "JobData"
        ? jobDataHistoryText(entry)
        : entry.entryType === "PhaseComplete"
          ? phaseCompletionHistoryText(entry)
          : entry.entryType === "MeasurementEdit"
            ? measurementEditHistoryText(entry)
            : entry.noteText;
  const action = entry.entryType === "Material" && canEditMaterialLots()
    ? `<button type="button" class="secondary compact-button" data-action="edit-material-lot">Edit Lot</button>`
    : completionMeasurements.length || completionJobData.length || completionMaterials.length || completionMetadata.length
      ? `<button type="button" class="secondary compact-button" data-action="details">Details</button>`
      : "";
  item.innerHTML = `
    <span>${formatDateTime(entry.timestamp)}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryTitle(entry))}</span>
    <span>${escapeHtml(details || "")}</span>
    <span>${escapeHtml(entry.resourceId || "-")}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryUser(entry))}</span>
    <span>${action}</span>`;
  item.querySelector("[data-action='details']")?.addEventListener("click", () => toggleReviewMeasurementGroup(container, completionGroupId));
  item.querySelector("[data-action='edit-material-lot']")?.addEventListener("click", () => editMaterialLot(entry, { refreshReview: true }));
  container.appendChild(item);
  completionMetadata.forEach((metadata) => renderReviewCompletionMetadataDetail(container, completionGroupId, metadata, entry));
  completionMaterials.forEach((material) => renderReviewCompletionMaterialDetail(container, completionGroupId, material, entry));
  completionJobData.forEach((jobData) => renderReviewJobDataDetail(container, completionGroupId, jobData, entry));
  completionMeasurements.forEach((measurement) => renderReviewMeasurementDetail(container, completionGroupId, measurement));
}

function inspectionCompletionMetadata(entry) {
  if (entry.entryType !== "PhaseComplete") {
    return [];
  }

  const rows = [];
  if (entry.machineCounter !== null && entry.machineCounter !== undefined) {
    rows.push({ label: "Machine Counter", value: entry.machineCounter });
  }

  return rows;
}

function renderReviewCompletionMetadataDetail(container, groupId, metadata, completionEntry) {
  const item = document.createElement("div");
  item.dataset.reviewGroup = groupId;
  item.className = "data-row review-job-data-detail-row hidden";
  item.innerHTML = `
    <span>${formatDateTime(completionEntry.timestamp)}</span>
    <span>-</span>
    <span>${escapeHtml(metadata.label)}<small>Inspection Data</small></span>
    <span>${escapeHtml(metadata.value)}</span>
    <span>${escapeHtml(completionEntry.resourceId || "-")}</span>
    <span>${escapeHtml(completionEntry.processCode || "-")}${completionEntry.operationSeq ? ` ${completionEntry.operationSeq}` : ""}</span>
    <span>${escapeHtml(historyEntryUser(completionEntry))}</span>
    <span></span>`;
  container.appendChild(item);
}

function inspectionCompletionMaterials(entry, materialEntries) {
  if (entry.entryType !== "PhaseComplete") {
    return [];
  }

  return materialEntries
    .filter((material) => reviewTraceabilityEventBelongsToCompletion(material, entry))
    .sort((a, b) => String(a.materialPartNum || "").localeCompare(String(b.materialPartNum || "")));
}

function renderReviewCompletionMaterialDetail(container, groupId, material, completionEntry) {
  const item = document.createElement("div");
  item.dataset.reviewGroup = groupId;
  item.className = "data-row review-job-data-detail-row hidden";
  item.innerHTML = `
    <span>${formatDateTime(material.timestamp)}</span>
    <span>-</span>
    <span>${escapeHtml(material.materialPartNum || "Material")}<small>Material</small></span>
    <span>${escapeHtml(material.newLotNum || "-")}</span>
    <span>${escapeHtml(material.resourceId || completionEntry.resourceId || "-")}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryUser(material))}</span>
    <span></span>`;
  container.appendChild(item);
}

function inspectionCompletionJobData(entry, jobDataEntries) {
  if (entry.entryType !== "PhaseComplete") {
    return [];
  }

  if (Array.isArray(entry.jobDataEntries)) {
    return [...entry.jobDataEntries].sort((a, b) => String(a.tagName || "").localeCompare(String(b.tagName || "")));
  }

  return jobDataEntries
    .filter((jobData) => reviewJobDataBelongsToCompletion(jobData, entry))
    .sort((a, b) => String(a.tagName || "").localeCompare(String(b.tagName || "")));
}

function renderReviewJobDataDetail(container, groupId, jobData, completionEntry) {
  const item = document.createElement("div");
  const isMaterialLot = isMaterialLotJobDataField(jobData.tagName);
  item.dataset.reviewGroup = groupId;
  item.className = "data-row review-job-data-detail-row hidden";
  item.innerHTML = `
    <span>${formatDateTime(jobData.timestamp)}</span>
    <span>-</span>
    <span>${escapeHtml(jobData.tagName || (isMaterialLot ? "Material" : "Job Data"))}<small>${isMaterialLot ? "Material" : "Job Data"}</small></span>
    <span>${escapeHtml(jobData.tagValue || "-")}</span>
    <span>${escapeHtml(jobData.resourceId || completionEntry.resourceId || "-")}</span>
    <span>-</span>
    <span>${escapeHtml(historyEntryUser(jobData))}</span>
    <span></span>`;
  container.appendChild(item);
}

function inspectionCompletionMeasurements(entry, measurementById) {
  if (entry.entryType !== "PhaseComplete" || !Array.isArray(entry.measurementIds)) {
    return [];
  }

  return entry.measurementIds
    .map((id) => measurementById.get(String(id).toLowerCase()))
    .filter(Boolean)
    .sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp));
}

function reviewMeasurementValueControl(row) {
  if (row.characteristicType === "Attribute") {
    return `
      <select class="review-measurement-value">
        <option value="1" ${Number(row.value) === 1 ? "selected" : ""}>Accept</option>
        <option value="0" ${Number(row.value) === 0 ? "selected" : ""}>Reject</option>
      </select>`;
  }

  return `<input class="review-measurement-value" type="number" step="0.00001" value="${Number(row.value)}">`;
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
    const rows = await api(`/qa/job-variable-means?jobNums=${encodeURIComponent(jobNums.join(","))}`);
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
      <span>Job</span><span>Context</span><span>Variable</span><span>Mean</span><span>Range</span><span>Std Dev</span><span>Capability</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "data-row";
    item.innerHTML = `
      <span>${row.jobNum}</span>
      <span>${row.processCode || ""} ${row.operationSeq || ""}<small>${row.inspectionPhases || ""}</small></span>
      <span>${row.characteristicName} (${row.unitOfMeasure})</span>
      <span>${formatNumber(row.mean)}</span>
      <span>${formatNumber(row.min)} - ${formatNumber(row.max)}</span>
      <span>${formatNumber(row.stdDev)}</span>
      <span>Cpk ${capabilityBadge(row.cpk)}<small>Ppk ${capabilityBadge(row.ppk)}</small></span>`;
    container.appendChild(item);
  });
}

async function loadTopIssues(event) {
  event?.preventDefault();
  try {
    const rows = await api("/history/top-issues", {
      method: "POST",
      body: JSON.stringify({
        partNum: $("topIssuesPartNum").value.trim() || null,
        jobNum: $("topIssuesJobNum").value.trim() || null,
        resourceId: $("topIssuesResourceId").value || null,
        operatorShift: $("topIssuesShift").value || null,
        characteristicName: $("topIssuesCharacteristicName").value.trim() || null,
        from: dateTimeLocalValue("topIssuesFrom"),
        to: dateTimeLocalValue("topIssuesTo"),
        limit: Number($("topIssuesLimit").value) || 25
      })
    });
    const issueGroups = collapseTopIssueRows(rows);
    renderTopIssues(issueGroups);
    $("topIssuesMessage").textContent = `${issueGroups.length} issue group${issueGroups.length === 1 ? "" : "s"} loaded.`;
    $("topIssuesMessage").className = "message ok";
  } catch (error) {
    $("topIssuesMessage").textContent = readableError(error);
    $("topIssuesMessage").className = "message error";
  }
}

function renderTopIssues(rows) {
  const container = $("topIssuesList");
  if (!rows.length) {
    container.className = "data-table empty";
    container.textContent = "No out-of-spec, drift, or rejected-attribute events found for those filters.";
    return;
  }

  container.className = "top-issues-list";
  container.innerHTML = `
    <div class="top-issue-header">
      <span>Issue</span><span>Events</span><span>Top Signal</span><span>Top Cause</span><span>Scope</span><span>Latest</span><span>Last Solution</span>
    </div>`;
  rows.forEach((row) => {
    const item = document.createElement("div");
    item.className = "top-issue-card";
    item.innerHTML = `
      <div class="top-issue-main">
        <span><strong>${escapeHtml(row.characteristicName)}</strong><small>Part ${escapeHtml(row.partNum)}</small></span>
        <span><strong>${row.eventCount}</strong>${row.activeCount ? `<small>${row.activeCount} active</small>` : ""}</span>
        <span>${escapeHtml(topBreakdownLabel(row.signalBreakdown, row.signalSummary || ruleLabel(row.ruleTriggered)))}</span>
        <span>${escapeHtml(topBreakdownLabel(row.causeBreakdown, row.causeCategory || "Unspecified"))}</span>
        <span>${row.distinctJobCount} job${row.distinctJobCount === 1 ? "" : "s"}<small>${row.distinctMachineCount} machine${row.distinctMachineCount === 1 ? "" : "s"}</small></span>
        <span>${escapeHtml(row.latestJobNum)}<small>${escapeHtml(row.latestResourceId)}${row.latestOperatorShift ? ` / ${escapeHtml(row.latestOperatorShift)}` : ""} / ${formatDateTime(row.latestEventAt)}</small></span>
        <span>${escapeHtml(row.latestSolution || "")}</span>
      </div>
      <div class="top-issue-breakdowns">
        ${renderIssueBreakdown("Signals", row.signalBreakdown)}
        ${renderIssueBreakdown("Causes", row.causeBreakdown)}
        ${renderIssueBreakdown("Solutions", row.solutionBreakdown)}
      </div>`;
    container.appendChild(item);
  });
}

function collapseTopIssueRows(rows) {
  const groups = new Map();
  rows.forEach((row) => {
    const key = [
      row.partNum || "",
      row.characteristicName || ""
    ].join("|").toLowerCase();
    const existing = groups.get(key);
    if (!existing) {
      groups.set(key, {
        ...row,
        causeCounts: causeCountsFromRow(row),
        signalCounts: signalCountsFromRow(row),
        solutionCounts: solutionCountsFromRow(row)
      });
      return;
    }

    existing.eventCount += row.eventCount || 0;
    existing.activeCount += row.activeCount || 0;
    existing.distinctJobCount += row.distinctJobCount || 0;
    existing.distinctMachineCount += row.distinctMachineCount || 0;
    mergeCauseCounts(existing.causeCounts, causeCountsFromRow(row));
    mergeCauseCounts(existing.signalCounts, signalCountsFromRow(row));
    mergeCauseCounts(existing.solutionCounts, solutionCountsFromRow(row));
    existing.causeCategory = topCountLabel(existing.causeCounts);
    existing.signalSummary = topCountLabel(existing.signalCounts);

    if (new Date(row.latestEventAt) > new Date(existing.latestEventAt)) {
      existing.latestEventAt = row.latestEventAt;
      existing.latestJobNum = row.latestJobNum;
      existing.latestResourceId = row.latestResourceId;
      existing.latestOperatorShift = row.latestOperatorShift;
      existing.latestDetail = row.latestDetail;
      existing.latestSolution = row.latestSolution;
    } else if (!existing.latestSolution && row.latestSolution) {
      existing.latestSolution = row.latestSolution;
    }
  });

  return [...groups.values()]
    .map((row) => ({
      ...row,
      causeCategory: topCountLabel(row.causeCounts),
      signalSummary: row.signalSummary || topCountLabel(row.signalCounts),
      causeBreakdown: breakdownItems(row.causeCounts),
      signalBreakdown: row.signalBreakdown?.length ? row.signalBreakdown : breakdownItems(row.signalCounts),
      solutionBreakdown: row.solutionBreakdown?.length ? row.solutionBreakdown : breakdownItems(row.solutionCounts)
    }))
    .sort((a, b) =>
      (b.eventCount || 0) - (a.eventCount || 0) ||
      (b.activeCount || 0) - (a.activeCount || 0) ||
      new Date(b.latestEventAt) - new Date(a.latestEventAt));
}

function causeCountsFromRow(row) {
  if (row.causeBreakdown?.length) {
    return countsFromBreakdown(row.causeBreakdown);
  }
  return new Map([[row.causeCategory || "Unspecified", row.eventCount || 0]]);
}

function signalCountsFromRow(row) {
  if (row.signalBreakdown?.length) {
    return countsFromBreakdown(row.signalBreakdown);
  }
  const signal = row.signalSummary || ruleLabel(row.ruleTriggered) || "Unspecified";
  return new Map([[signal, row.eventCount || 0]]);
}

function solutionCountsFromRow(row) {
  if (row.solutionBreakdown?.length) {
    return countsFromBreakdown(row.solutionBreakdown);
  }
  return new Map([[row.latestSolution || "No solution entered", row.eventCount || 0]]);
}

function countsFromBreakdown(items) {
  const counts = new Map();
  items.forEach((item) => {
    const label = item.label || "Unspecified";
    counts.set(label, (counts.get(label) || 0) + (item.count || 0));
  });
  return counts;
}

function mergeCauseCounts(target, source) {
  source.forEach((count, cause) => {
    target.set(cause, (target.get(cause) || 0) + count);
  });
}

function breakdownItems(counts) {
  return [...counts.entries()]
    .filter(([, count]) => count > 0)
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([label, count]) => ({ label, count }));
}

function topCountLabel(counts) {
  const items = breakdownItems(counts);
  if (!items.length) {
    return "Unspecified";
  }
  return items[0].label;
}

function topBreakdownLabel(items, fallback) {
  return items?.length ? items[0].label : fallback;
}

function renderIssueBreakdown(title, items = []) {
  const rows = items.length ? items : [{ label: "Unspecified", count: 0 }];
  return `
    <section class="issue-breakdown-panel">
      <h4>${escapeHtml(title)}</h4>
      <div class="issue-breakdown-list">
        ${rows.map((item) => `
          <div class="issue-breakdown-row">
            <span>${escapeHtml(item.label)}</span>
            <strong>${item.count}</strong>
          </div>`).join("")}
      </div>
    </section>`;
}

function openJobSummaryCsv() {
  const jobNums = parseJobNums();
  if (!jobNums.length) {
    $("jobSummaryMessage").textContent = "Enter at least one job number.";
    $("jobSummaryMessage").className = "message error";
    return;
  }
  window.open(`/qa/job-variable-means.csv?jobNums=${encodeURIComponent(jobNums.join(","))}`, "_blank");
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

function renderMachines() {
  const list = $("machineList");
  const machines = state.snapshot?.resources || [];
  if (!machines.length) {
    list.className = "setup-list empty";
    list.textContent = "No machines configured.";
    newMachine();
    return;
  }

  list.className = "setup-list";
  list.innerHTML = "";
  machines
    .slice()
    .sort((a, b) => a.resourceId.localeCompare(b.resourceId))
    .forEach((resource) => {
      const row = document.createElement("button");
      row.type = "button";
      row.className = `setup-row user-list-row ${state.selectedResourceId.toLowerCase() === resource.resourceId.toLowerCase() ? "selected" : ""}`;
      row.innerHTML = `
        <span>
          <strong>${escapeHtml(resource.resourceId)}</strong>
          <span>${escapeHtml(resource.description || "No description")}</span>
        </span>`;
      row.addEventListener("click", () => selectMachine(resource.resourceId));
      list.appendChild(row);
    });

  if (!state.selectedResourceId || !machines.some((resource) => resource.resourceId.toLowerCase() === state.selectedResourceId.toLowerCase())) {
    selectMachine(machines[0].resourceId);
  }
}

function selectMachine(resourceId) {
  const resource = state.snapshot?.resources?.find((item) => item.resourceId.toLowerCase() === resourceId.toLowerCase());
  if (!resource) {
    newMachine();
    return;
  }

  state.selectedResourceId = resource.resourceId;
  $("machineDetailTitle").textContent = `Edit ${resource.resourceId}`;
  $("setupResourceId").value = resource.resourceId;
  $("setupOriginalResourceId").value = resource.resourceId;
  $("setupResourceDescription").value = resource.description || "";
  $("setupDeviceProfile").value = resource.deviceProfile || "keyboard";
  $("setupSerialBaudRate").value = String(resource.serialBaudRate || 9600);
  $("deleteSelectedMachineButton").disabled = false;
  renderMachines();
}

function newMachine() {
  state.selectedResourceId = "";
  $("machineDetailTitle").textContent = "New Machine";
  $("setupResourceId").value = "";
  $("setupOriginalResourceId").value = "";
  $("setupResourceDescription").value = "";
  $("setupDeviceProfile").value = "keyboard";
  $("setupSerialBaudRate").value = "9600";
  $("deleteSelectedMachineButton").disabled = true;
  renderMachinesWithoutSelection();
}

function renderMachinesWithoutSelection() {
  const selected = state.selectedResourceId;
  state.selectedResourceId = "";
  const list = $("machineList");
  if (!list || !(state.snapshot?.resources || []).length) return;
  list.querySelectorAll(".user-list-row").forEach((row) => row.classList.remove("selected"));
  state.selectedResourceId = selected;
}

async function saveMachine(event) {
  event.preventDefault();
  try {
    const result = await api("/setup/resources", {
      method: "POST",
      body: JSON.stringify({
        resourceId: $("setupResourceId").value.trim(),
        description: $("setupResourceDescription").value.trim(),
        originalResourceId: $("setupOriginalResourceId").value.trim() || null,
        deviceProfile: $("setupDeviceProfile").value,
        serialBaudRate: Number($("setupSerialBaudRate").value)
      })
    });
    await refreshMachines(result.resourceId);
    $("machineSetupMessage").textContent = "Machine saved.";
    $("machineSetupMessage").className = "message ok";
  } catch (error) {
    $("machineSetupMessage").textContent = readableError(error);
    $("machineSetupMessage").className = "message error";
  }
}

async function deleteSelectedMachine() {
  const resourceId = $("setupOriginalResourceId").value.trim();
  if (!resourceId) {
    return;
  }

  try {
    await api(`/setup/resources/${encodeURIComponent(resourceId)}`, { method: "DELETE" });
    await refreshMachines("");
    newMachine();
    $("machineSetupMessage").textContent = "Machine deleted.";
    $("machineSetupMessage").className = "message ok";
  } catch (error) {
    $("machineSetupMessage").textContent = readableError(error);
    $("machineSetupMessage").className = "message error";
  }
}

async function refreshMachines(selectedResourceId) {
  state.snapshot.resources = await api("/setup/resources");
  state.selectedResourceId = selectedResourceId || "";
  refreshMachineDropdowns();
  renderMachines();
}

function refreshMachineDropdowns() {
  const currentMachine = $("resourceId").value;
  fillSelect($("resourceId"), [{ resourceId: "", description: "Select machine" }, ...state.snapshot.resources], (resource) => resource.resourceId, (resource) => resource.resourceId || resource.description);
  if (state.snapshot.resources.some((resource) => resource.resourceId === currentMachine)) {
    $("resourceId").value = currentMachine;
  }
  renderReportControls();
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
    const shiftText = user.shift || "Unassigned shift";
    row.innerHTML = `
      <div>
        <strong>${user.userName}</strong>
        <span>${user.roles.join(", ")} · ${escapeHtml(shiftText)}</span>
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
    shift: "",
    roles: [assignableRoles()[0] || ""],
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
  const role = user?.roles?.[0] || assignableRoles()[0] || "";
  if (role && ![...$("setupRole").options].some((option) => option.value === role)) {
    const option = document.createElement("option");
    option.value = role;
    option.textContent = role;
    option.disabled = true;
    $("setupRole").appendChild(option);
  }
  $("setupRole").value = role;
  setShiftSelection(user?.shift || "");
  setUserProductGroupSelection(user?.productGroups || []);
  $("resetSelectedUserPasswordButton").classList.toggle("hidden", !hasUser || isNew);
  $("deleteSelectedUserButton").classList.toggle("hidden", !hasUser || isNew);
  $("userSetupForm").querySelector("button[type='submit']").classList.toggle("hidden", !hasUser);
}

function setShiftSelection(shift) {
  const select = $("setupShift");
  const normalized = shift || "";
  if (normalized && ![...select.options].some((option) => option.value === normalized)) {
    const option = document.createElement("option");
    option.value = normalized;
    option.textContent = normalized;
    select.appendChild(option);
  }
  select.value = normalized;
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
    await api(`/setup/users/${encodeURIComponent(userName)}?${actingSessionQuery()}`, { method: "DELETE" });
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
      body: JSON.stringify({ userName, temporaryPassword, actingUserName: state.user?.userName || "", actingSessionToken: state.user?.sessionToken || "" })
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
        shift: $("setupShift").value,
        roles: [$("setupRole").value],
        productGroups: selectedUserProductGroups(),
        actingUserName: state.user?.userName || "",
        actingSessionToken: state.user?.sessionToken || ""
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
  if (!canImportSetupData()) {
    $("userImportMessage").textContent = "Import is restricted to GOD access.";
    $("userImportMessage").className = "message error";
    return;
  }

  const file = $("userImportFile").files[0];
  if (!file) {
    $("userImportMessage").textContent = "Select a user permissions workbook to import.";
    $("userImportMessage").className = "message error";
    return;
  }

  try {
    const formData = new FormData();
    formData.append("file", file);
    const result = await api(`/setup/users/import-xlsx?${actingSessionQuery()}`, {
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
    <label class="setup-order-field"><span>Order</span><input class="setup-display-order" type="number" min="1" step="1"></label>
    <label class="setup-name-field"><span>Inspection item</span><input class="setup-characteristic-name" required></label>
    <label class="setup-type-field">
      <span>Type</span>
      <select class="setup-characteristic-type">
        <option value=""></option>
        <option value="Variable">Measured</option>
        <option value="Attribute">Accept / Reject</option>
      </select>
    </label>
    <label class="setup-unit-field"><span>Unit</span><input class="setup-unit" required></label>
    <label class="setup-location-field"><span>Requirement / context</span><input class="setup-location"></label>
    <label class="setup-method-field"><span>Method / tool</span><input class="setup-method"></label>
    <section class="setup-phase-matrix">
      <h4>Phase requirements</h4>
      <div class="setup-phase-grid">
        <div class="setup-phase-grid-header"><span>Phase</span><span>Use</span><span>Sample</span><span>Frequency type</span><span>Frequency</span><span>Unit</span><span>Rule</span></div>
        ${INSPECTION_PHASES.map((phase) => setupPhaseRowTemplate(phase)).join("")}
      </div>
    </section>
    <label class="numeric-setup-field"><span>Target</span><input class="setup-nominal" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LSL</span><input class="setup-lsl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>USL</span><input class="setup-usl" type="number" step="0.0001" required></label>
    <label class="numeric-setup-field"><span>LCL</span><input class="setup-lcl" type="number" step="0.0001"></label>
    <label class="numeric-setup-field"><span>UCL</span><input class="setup-ucl" type="number" step="0.0001"></label>
    <div class="setup-row-actions">
      <button type="button" class="secondary compact-button move-variable-up-button">Up</button>
      <button type="button" class="secondary compact-button move-variable-down-button">Down</button>
    </div>
    <button type="button" class="secondary compact-button remove-variable-button">Remove</button>`;
}

function setupPhaseRowTemplate(phase) {
  return `
    <div class="setup-phase-row" data-phase="${escapeHtml(phase)}">
      <span>${escapeHtml(phase)}</span>
      <input class="setup-phase-required" type="checkbox" aria-label="${escapeHtml(phase)} required">
      <input class="setup-phase-sample-size" type="number" min="1" aria-label="${escapeHtml(phase)} sample size">
      <select class="setup-phase-frequency-type" aria-label="${escapeHtml(phase)} frequency type">
        <option value=""></option>
        <option value="Quantity">Quantity</option>
        <option value="Time">Time</option>
        <option value="Event">Event</option>
      </select>
      <input class="setup-phase-frequency-value" type="number" min="1" aria-label="${escapeHtml(phase)} frequency">
      <select class="setup-phase-frequency-unit" aria-label="${escapeHtml(phase)} frequency unit"></select>
      <select class="setup-phase-alert-rule-set" aria-label="${escapeHtml(phase)} drift rule">
        <option value=""></option>
        <option value="GlobalDefault">Use Global Default</option>
        <option value="WesternElectric">Western Electric</option>
        <option value="NelsonRules">Nelson Rules</option>
        <option value="Cusum">CUSUM</option>
        <option value="Ewma">EWMA</option>
        <option value="MovingAverageTrend">Moving Average Trend</option>
        <option value="LinearTrendSlope">Linear Trend / Slope</option>
        <option value="Custom">Custom Rule</option>
        <option value="SpecLimitOnly">Spec Limit Only</option>
        <option value="None">No Automatic Rule</option>
      </select>
    </div>`;
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

function addSetupVariableRow(values = {}, type = values.characteristicType || "") {
  const row = document.createElement("div");
  row.className = "setup-variable-row";
  row.dataset.originalCharacteristicName = values.characteristicName || "";
  row.innerHTML = setupVariableRowTemplate();
  row.querySelector(".setup-display-order").value = values.displayOrder ?? "";
  row.querySelector(".setup-characteristic-name").value = values.characteristicName || "";
  row.querySelector(".setup-characteristic-type").value = type;
  row.querySelector(".setup-unit").value = values.unitOfMeasure || "";
  row.querySelector(".setup-location").value = values.location || "";
  row.querySelector(".setup-method").value = values.inspectionMethod || "";
  row.querySelector(".setup-nominal").value = values.nominal ?? "";
  row.querySelector(".setup-lsl").value = values.lsl ?? "";
  row.querySelector(".setup-usl").value = values.usl ?? "";
  row.querySelector(".setup-lcl").value = values.lcl ?? "";
  row.querySelector(".setup-ucl").value = values.ucl ?? "";
  row.querySelector(".setup-characteristic-type").addEventListener("change", () => updateSetupVariableType(row));
  row.querySelector(".setup-display-order").addEventListener("change", () => moveSetupVariableRowToPosition(row, Number(row.querySelector(".setup-display-order").value)));
  row.querySelectorAll(".setup-phase-row").forEach((phaseRow) => {
    phaseRow.querySelector(".setup-phase-frequency-type").addEventListener("change", () => updatePhaseFrequencyUnits(phaseRow));
  });
  populatePhaseRows(row, values.phaseSettings || phaseSettingsFromPlan(values));
  row.querySelector(".remove-variable-button").addEventListener("click", () => {
    const container = row.parentElement;
    if (container?.children.length === 1 && container.id === "setupVariableRows") {
      row.querySelectorAll("input").forEach((input) => { input.value = ""; });
      row.querySelectorAll(".setup-phase-row").forEach((phaseRow) => {
        phaseRow.querySelector(".setup-phase-required").checked = false;
        phaseRow.querySelector(".setup-phase-frequency-type").value = "";
        phaseRow.querySelector(".setup-phase-frequency-value").value = "";
        phaseRow.querySelector(".setup-phase-alert-rule-set").value = "";
        updatePhaseFrequencyUnits(phaseRow, "");
      });
      updateSetupVariableOrderInputs();
      return;
    }
    row.remove();
    updateSetupVariableOrderInputs();
  });
  $("setupVariableRows").appendChild(row);
  row.querySelector(".move-variable-up-button").addEventListener("click", () => moveSetupVariableRow(row, -1));
  row.querySelector(".move-variable-down-button").addEventListener("click", () => moveSetupVariableRow(row, 1));
  updateSetupVariableOrderInputs();
  updateSetupVariableType(row);
}

function setupVariableRowElements() {
  return [...document.querySelectorAll(".setup-variable-row")];
}

function updateSetupVariableOrderInputs() {
  setupVariableRowElements().forEach((row, index) => {
    row.querySelector(".setup-display-order").value = String(index + 1);
  });
}

function moveSetupVariableRow(row, delta) {
  const rows = setupVariableRowElements();
  const currentIndex = rows.indexOf(row);
  const nextIndex = currentIndex + delta;
  if (currentIndex < 0 || nextIndex < 0 || nextIndex >= rows.length) {
    return;
  }

  if (delta < 0) {
    rows[nextIndex].before(row);
  } else {
    rows[nextIndex].after(row);
  }
  updateSetupVariableOrderInputs();
}

function moveSetupVariableRowToPosition(row, requestedPosition) {
  const rows = setupVariableRowElements().filter((item) => item !== row);
  const boundedIndex = Math.max(0, Math.min(rows.length, Math.trunc(requestedPosition || rows.length + 1) - 1));
  if (boundedIndex >= rows.length) {
    $("setupVariableRows").appendChild(row);
  } else {
    rows[boundedIndex].before(row);
  }
  updateSetupVariableOrderInputs();
}

function phaseSettingsFromPlan(plan) {
  if (!plan.inspectionPhase) {
    return [];
  }

  return [{
    inspectionPhase: plan.inspectionPhase,
    sampleSize: plan.sampleSize,
    frequencyType: plan.frequencyType,
    frequencyValue: plan.frequencyValue,
    frequencyUnit: plan.frequencyUnit,
    alertRuleSet: plan.alertRuleSet
  }];
}

function populatePhaseRows(row, phaseSettings = []) {
  const byPhase = new Map(phaseSettings.map((phase) => [normalizeInspectionPhase(phase.inspectionPhase), phase]));
  row.querySelectorAll(".setup-phase-row").forEach((phaseRow) => {
    const phase = phaseRow.dataset.phase;
    const setting = byPhase.get(normalizeInspectionPhase(phase));
    phaseRow.querySelector(".setup-phase-required").checked = Boolean(setting);
    phaseRow.querySelector(".setup-phase-sample-size").value = setting?.sampleSize ?? "";
    phaseRow.querySelector(".setup-phase-frequency-type").value = setting?.frequencyType || "";
    phaseRow.querySelector(".setup-phase-frequency-value").value = setting?.frequencyValue ?? "";
    phaseRow.querySelector(".setup-phase-alert-rule-set").value = setting?.alertRuleSet || "";
    updatePhaseFrequencyUnits(phaseRow, setting?.frequencyUnit || "");
  });
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

  const set = setupInspectionSets().find((item) => item.key === key);
  if (!set) {
    return;
  }

  $("setupPartNum").value = set.partNum;
  $("setupPartDescription").value = set.partDescription;
  $("setupProductGroup").value = set.productGroup || "General";
  $("setupBlankCode").value = set.blankCode || "";
  $("setupHoleSize").value = set.holeSize || "";
  $("setupProcessCode").value = set.processCode;
  $("setupProcessDescription").value = set.processDescription || set.processCode;
  $("setupOperationSeq").value = String(set.operationSeq || 10);
  state.editingSetup = {
    processCode: set.processCode,
    operationSeq: set.operationSeq || 10
  };
  const groupedPlans = setupVariableGroups(set.plans);
  const firstPlan = groupedPlans[0]?.plans[0] || set.plans[0];
  $("setupInspectionPhase").value = "";
  $("setupSampleSize").value = "";
  $("setupFrequencyType").value = "";
  updateSetupFrequencyUnits();
  $("setupFrequencyValue").value = "";
  $("setupFrequencyUnit").value = "";
  $("setupAlertRuleSet").value = "";
  updateRuleDescription();
  $("setupVariableRows").innerHTML = "";
  groupedPlans.forEach((group) => addSetupVariableRow(group.master, group.master.characteristicType));
  $("setupJobDataFieldRows").innerHTML = "";
  (state.snapshot.partJobDataFields || [])
    .filter((field) =>
      field.partNum.toLowerCase() === set.partNum.toLowerCase() &&
      normalizeInspectionPhase(field.inspectionPhase) === normalizeInspectionPhase(firstPlan.inspectionPhase) &&
      !isBuiltInOrPartStandardJobData(field.fieldName))
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

function setupVariableGroups(plans) {
  const groups = new Map();
  plans.forEach((plan) => {
    const key = `${plan.characteristicName}|${plan.characteristicType}`.toLowerCase();
    if (!groups.has(key)) {
      groups.set(key, {
        master: {
          ...plan,
          phaseSettings: []
        },
        plans: []
      });
    }
    const group = groups.get(key);
    group.plans.push(plan);
    group.master.phaseSettings.push(phaseSettingsFromPlan(plan)[0]);
  });

  return [...groups.values()]
    .map((group) => {
      group.master.phaseSettings = group.master.phaseSettings
        .filter(Boolean)
        .sort((a, b) => INSPECTION_PHASES.indexOf(normalizeInspectionPhase(a.inspectionPhase)) - INSPECTION_PHASES.indexOf(normalizeInspectionPhase(b.inspectionPhase)));
      return group;
    })
    .sort((a, b) => (a.master.displayOrder ?? 0) - (b.master.displayOrder ?? 0) || a.master.characteristicName.localeCompare(b.master.characteristicName));
}

function clearInspectionSetupForm() {
  $("inspectionSetupForm").reset();
  $("setupEditPartSelect").value = "";
  state.editingSetup = null;
  $("setupOperationSeq").value = "10";
  $("setupProcessDescription").value = "";
  $("setupSampleSize").value = "";
  $("setupProductGroup").value = "";
  $("setupBlankCode").value = "";
  $("setupHoleSize").value = "";
  $("setupFrequencyType").value = "";
  $("setupFrequencyValue").value = "";
  $("setupFrequencyUnit").value = "";
  $("setupAlertRuleSet").value = "";
  $("setupInspectionPhase").value = "";
  updateRuleDescription();
  updateSetupFrequencyUnits();
  $("setupVariableRows").innerHTML = "";
  $("setupJobDataFieldRows").innerHTML = "";
  $("setupMaterialRows").innerHTML = "";
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

function updatePhaseFrequencyUnits(row, requestedUnit = null) {
  const unitsByType = {
    Quantity: [["Pieces", "Pieces"], ["Box", "Box"]],
    Time: [["Minutes", "Minutes"], ["Hours", "Hours"]],
    Event: [["StartOfJob", "Start of job"], ["MaterialChange", "Material change"], ["ToolChange", "Tool change"], ["Restart", "Restart"]]
  };
  const current = requestedUnit || row.querySelector(".setup-phase-frequency-unit").value;
  const frequencyType = row.querySelector(".setup-phase-frequency-type").value;
  const units = frequencyType ? unitsByType[frequencyType] || [] : [];
  fillSelect(row.querySelector(".setup-phase-frequency-unit"), units, (unit) => unit[0], (unit) => unit[1]);
  if (units.some((unit) => unit[0] === current)) {
    row.querySelector(".setup-phase-frequency-unit").value = current;
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

function openGettingStarted() {
  $("gettingStartedModal").classList.remove("hidden");
}

function closeGettingStarted() {
  $("gettingStartedModal").classList.add("hidden");
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
  const frequencyType = $("setupFrequencyType").value;
  const units = frequencyType ? unitsByType[frequencyType] || [] : [];
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
    phaseSettings: setupPhaseRows(row),
    nominal: Number(row.querySelector(".setup-nominal").value),
    lsl: Number(row.querySelector(".setup-lsl").value),
    usl: Number(row.querySelector(".setup-usl").value),
    lcl: optionalInputNumber(row.querySelector(".setup-lcl")),
    ucl: optionalInputNumber(row.querySelector(".setup-ucl")),
    displayOrder: Number(row.querySelector(".setup-display-order").value) || index + 1
  }));
}

function setupPhaseRows(row) {
  return [...row.querySelectorAll(".setup-phase-row")]
    .map((phaseRow) => ({
      inspectionPhase: phaseRow.dataset.phase,
      isRequired: phaseRow.querySelector(".setup-phase-required").checked,
      sampleSize: Number(phaseRow.querySelector(".setup-phase-sample-size").value),
      frequencyType: phaseRow.querySelector(".setup-phase-frequency-type").value,
      frequencyValue: Number(phaseRow.querySelector(".setup-phase-frequency-value").value),
      frequencyUnit: phaseRow.querySelector(".setup-phase-frequency-unit").value,
      alertRuleSet: phaseRow.querySelector(".setup-phase-alert-rule-set").value
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
  const phaseError = validateVariablePhases(variables);
  if (phaseError) {
    $("inspectionSetupMessage").textContent = phaseError;
    $("inspectionSetupMessage").className = "message error";
    return;
  }

  try {
    const baseRequest = {
      partNum: $("setupPartNum").value.trim(),
      partDescription: $("setupPartDescription").value.trim(),
      productGroup: $("setupProductGroup").value.trim(),
      blankCode: $("setupBlankCode").value.trim(),
      holeSize: $("setupHoleSize").value.trim(),
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
      for (const phase of variable.phaseSettings.filter((item) => !item.isRequired)) {
        await api("/setup/inspection-plans/delete-phase", {
          method: "POST",
          body: JSON.stringify({
            partNum: baseRequest.partNum,
            processCode: baseRequest.processCode,
            operationSeq: baseRequest.operationSeq,
            characteristicName: variable.characteristicName,
            inspectionPhase: phase.inspectionPhase,
            originalProcessCode: state.editingSetup?.processCode || null,
            originalOperationSeq: state.editingSetup?.operationSeq || null,
            originalCharacteristicName: variable.originalCharacteristicName
          })
        });
      }

      for (const phase of variable.phaseSettings.filter((item) => item.isRequired)) {
        await api("/setup/inspection-plans", {
          method: "POST",
          body: JSON.stringify({
            ...baseRequest,
            inspectionPhase: phase.inspectionPhase,
            alertRuleSet: phase.alertRuleSet,
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
            sampleSize: phase.sampleSize,
            frequencyType: phase.frequencyType,
            frequencyValue: phase.frequencyValue,
            frequencyUnit: phase.frequencyUnit,
            originalProcessCode: state.editingSetup?.processCode || null,
            originalOperationSeq: state.editingSetup?.operationSeq || null,
            originalCharacteristicName: variable.originalCharacteristicName
          })
        });
      }
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

function validateVariablePhases(variables) {
  for (const variable of variables) {
    const requiredPhases = variable.phaseSettings.filter((phase) => phase.isRequired);
    if (!requiredPhases.length) {
      return `${variable.characteristicName || "Inspection item"} needs at least one required phase.`;
    }

    const invalidPhase = requiredPhases.find((phase) =>
      !phase.sampleSize ||
      !phase.frequencyType ||
      !phase.frequencyValue ||
      !phase.frequencyUnit ||
      !phase.alertRuleSet);
    if (invalidPhase) {
      return `${variable.characteristicName} is missing timing or rule settings for ${invalidPhase.inspectionPhase}.`;
    }
  }

  return "";
}

async function importPartsXlsx(event) {
  event.preventDefault();
  if (!canImportSetupData()) {
    $("partsImportMessage").textContent = "Import is restricted to GOD access.";
    $("partsImportMessage").className = "message error";
    return;
  }

  const file = $("partsImportFile").files[0];
  if (!file) {
    $("partsImportMessage").textContent = "Select a parts and inspections workbook to import.";
    $("partsImportMessage").className = "message error";
    return;
  }

  try {
    const formData = new FormData();
    formData.append("file", file);
    await api("/setup/import-xlsx", {
      method: "POST",
      body: formData
    });
    $("partsImportMessage").textContent = "Parts and inspections workbook imported.";
    $("partsImportMessage").className = "message ok";
    $("partsImportFile").value = "";
    await loadSnapshot();
  } catch (error) {
    $("partsImportMessage").textContent = readableError(error);
    $("partsImportMessage").className = "message error";
  }
}

async function importMachinesXlsx(event) {
  event.preventDefault();
  if (!canImportSetupData()) {
    $("machineImportMessage").textContent = "Import is restricted to GOD access.";
    $("machineImportMessage").className = "message error";
    return;
  }

  const file = $("machineImportFile").files[0];
  if (!file) {
    $("machineImportMessage").textContent = "Select a machine workbook to import.";
    $("machineImportMessage").className = "message error";
    return;
  }

  try {
    const formData = new FormData();
    formData.append("file", file);
    const result = await api("/setup/resources/import-xlsx", {
      method: "POST",
      body: formData
    });
    $("machineImportMessage").textContent = `${result.count} machines imported.`;
    $("machineImportMessage").className = "message ok";
    $("machineImportFile").value = "";
    await refreshMachines(state.selectedResourceId);
  } catch (error) {
    $("machineImportMessage").textContent = readableError(error);
    $("machineImportMessage").className = "message error";
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

function exportSetupTemplate() {
  window.open("/setup/export-template.csv", "_blank");
}

function exportMachineTemplate() {
  window.open("/setup/resources/export.csv", "_blank");
}

function exportUserTemplate() {
  window.open("/setup/users/export.csv", "_blank");
}

function archiveCutoffIso() {
  const value = $("archiveCutoffDate").value;
  return value ? new Date(`${value}T00:00:00`).toISOString() : "";
}

function archiveCountRows(counts) {
  return [
    ["Measurements", counts.measurements],
    ["Edited measurements", counts.measurementEditAudits],
    ["Job notes", counts.jobNotes],
    ["Inspection completions", counts.jobPhaseCompletions],
    ["Job data tags", counts.jobTags],
    ["Locks", counts.alerts],
    ["Rule violations", counts.ruleViolations],
    ["Lock clear records", counts.alertOverrides],
    ["Material changes", counts.materialChanges]
  ];
}

function renderArchiveCounts(counts) {
  $("archiveCounts").innerHTML = archiveCountRows(counts)
    .map(([label, value]) => `<div class="archive-count-card"><span>${label}</span><strong>${value}</strong></div>`)
    .join("");
}

async function createManualBackup() {
  const button = $("createManualBackupButton");
  button.disabled = true;
  $("manualBackupMessage").textContent = "Creating backup...";
  $("manualBackupMessage").className = "message";
  $("manualBackupResultPanel").classList.add("hidden");
  try {
    const result = await api("/setup/backups/manual", {
      method: "POST",
      body: JSON.stringify({
        userName: $("manualBackupUserName").value.trim(),
        password: $("manualBackupPassword").value
      })
    });
    $("manualBackupPassword").value = "";
    $("manualBackupMessage").textContent = "Backup created.";
    $("manualBackupMessage").className = "message ok";
    $("manualBackupResultPanel").classList.remove("hidden");
    $("manualBackupResultPanel").innerHTML = `
      <h3>Backup File</h3>
      <p><strong>${escapeHtml(result.backupFileName)}</strong></p>
      <p>${escapeHtml(result.backupPath)}</p>
      <a class="secondary archive-download-link" href="${result.downloadPath}" download>Download backup file</a>
    `;
  } catch (error) {
    $("manualBackupMessage").textContent = readableError(error);
    $("manualBackupMessage").className = "message error";
  } finally {
    button.disabled = false;
  }
}

function clearHistoryCredentials() {
  return {
    userName: $("clearHistoryUserName").value.trim(),
    password: $("clearHistoryPassword").value,
    confirmationText: $("clearHistoryConfirmationText").value.trim()
  };
}

function restoreBackupCredentials() {
  return {
    userName: $("restoreBackupUserName").value.trim(),
    password: $("restoreBackupPassword").value,
    confirmationText: $("restoreBackupConfirmationText").value.trim()
  };
}

function showDatabaseActionResult(title, rows) {
  $("databaseActionResultPanel").classList.remove("hidden");
  $("databaseActionResultPanel").innerHTML = `
    <h3>${escapeHtml(title)}</h3>
    ${rows.map(([label, value]) => `<p><strong>${escapeHtml(label)}:</strong> ${escapeHtml(value)}</p>`).join("")}
  `;
}

async function clearHistoryDataForRestoreTest() {
  const button = $("clearDatabaseButton");
  button.disabled = true;
  $("clearHistoryMessage").textContent = "Clearing history data...";
  $("clearHistoryMessage").className = "message";
  $("databaseActionResultPanel").classList.add("hidden");
  try {
    const result = await api("/setup/database/clear-history", {
      method: "POST",
      body: JSON.stringify(clearHistoryCredentials())
    });
    $("clearHistoryPassword").value = "";
    $("clearHistoryConfirmationText").value = "";
    $("clearHistoryMessage").textContent = "History data cleared. Users, machines, parts, inspections, rules, and setup data were kept.";
    $("clearHistoryMessage").className = "message ok";
    $("restoreBackupMessage").textContent = "";
    $("restoreBackupMessage").className = "message";
    showDatabaseActionResult("History Data Cleared", [
      ["Quarantine file", result.quarantineFileName],
      ["Quarantine path", result.quarantinePath]
    ]);
    await loadSetupAdmin();
    await loadSnapshot();
  } catch (error) {
    $("clearHistoryMessage").textContent = readableError(error);
    $("clearHistoryMessage").className = "message error";
  } finally {
    button.disabled = false;
  }
}

async function restoreLatestBackup() {
  const button = $("restoreLatestBackupButton");
  button.disabled = true;
  $("restoreBackupMessage").textContent = "Restoring latest backup...";
  $("restoreBackupMessage").className = "message";
  $("databaseActionResultPanel").classList.add("hidden");
  try {
    const result = await api("/setup/database/restore-latest", {
      method: "POST",
      body: JSON.stringify(restoreBackupCredentials())
    });
    $("restoreBackupPassword").value = "";
    $("restoreBackupConfirmationText").value = "";
    $("restoreBackupMessage").textContent = "Latest backup restored.";
    $("restoreBackupMessage").className = "message ok";
    $("clearHistoryMessage").textContent = "";
    $("clearHistoryMessage").className = "message";
    showDatabaseActionResult("Database Restored", [
      ["Restored backup", result.restoredBackupFileName],
      ["Backup path", result.restoredBackupPath],
      ["Quarantine file", result.quarantineFileName],
      ["Quarantine path", result.quarantinePath]
    ]);
    await loadSetupAdmin();
    await loadSnapshot();
  } catch (error) {
    $("restoreBackupMessage").textContent = readableError(error);
    $("restoreBackupMessage").className = "message error";
  } finally {
    button.disabled = false;
  }
}

async function previewArchive(event) {
  event.preventDefault();
  $("archiveMessage").textContent = "";
  $("archiveMessage").className = "message";
  $("archiveResultPanel").classList.add("hidden");
  try {
    const cutoffDate = archiveCutoffIso();
    if (!cutoffDate) {
      throw new Error("Archive cutoff date is required.");
    }

    const preview = await api("/setup/archive/preview", {
      method: "POST",
      body: JSON.stringify({ cutoffDate })
    });
    renderArchiveCounts(preview.counts);
    $("archivePreviewPanel").classList.remove("hidden");
    $("archiveWarning").textContent = preview.activeLocksBeforeCutoff > 0
      ? `${preview.activeLocksBeforeCutoff} active lock(s) exist before this cutoff. Clear those locks before archiving.`
      : "Review these counts before creating the archive. This will remove matching records from the live database after the archive file is written.";
    $("archiveWarning").className = preview.activeLocksBeforeCutoff > 0 ? "message error" : "message";
  } catch (error) {
    $("archivePreviewPanel").classList.add("hidden");
    $("archiveMessage").textContent = readableError(error);
    $("archiveMessage").className = "message error";
  }
}

async function createArchive() {
  const button = $("createArchiveButton");
  button.disabled = true;
  $("archiveMessage").textContent = "Creating archive...";
  $("archiveMessage").className = "message";
  try {
    const result = await api("/setup/archive", {
      method: "POST",
      body: JSON.stringify({
        cutoffDate: archiveCutoffIso(),
        archiveUserName: $("archiveUserName").value.trim(),
        archivePassword: $("archivePassword").value,
        confirmationText: $("archiveConfirmationText").value.trim()
      })
    });
    renderArchiveCounts(result.counts);
    $("archiveMessage").textContent = "Archive created and live records were removed.";
    $("archiveMessage").className = "message ok";
    $("archivePassword").value = "";
    $("archiveConfirmationText").value = "";
    $("archiveResultPanel").classList.remove("hidden");
    $("archiveResultPanel").innerHTML = `
      <h3>Archive File</h3>
      <p><strong>${escapeHtml(result.archiveFileName)}</strong></p>
      <p>${escapeHtml(result.archivePath)}</p>
      <a class="secondary archive-download-link" href="${result.downloadPath}" download>Download archive file</a>
    `;
    await loadSnapshot();
  } catch (error) {
    $("archiveMessage").textContent = readableError(error);
    $("archiveMessage").className = "message error";
  } finally {
    button.disabled = false;
  }
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
  clearSelectedWorkContext();
});
$("partNum").addEventListener("input", () => {
  refreshOperationChoices({ preserve: false });
  clearSelectedWorkContext();
});
$("operationCode").addEventListener("change", clearSelectedWorkContext);
$("inspectionPhase").addEventListener("change", clearSelectedWorkContext);
$("resourceId").addEventListener("change", clearSelectedWorkContext);
$("logoutButton").addEventListener("click", logout);
$("measurementForm").addEventListener("submit", submitMeasurement);
$("machineCounter").addEventListener("input", () => {
  normalizeMachineCounterInput();
  updateInspectionSubmitState();
});
$("jobTagsForm").addEventListener("submit", saveJobTags);
$("materialChangeForm").addEventListener("submit", saveMaterialChange);
$("jobNoteForm").addEventListener("submit", saveJobNote);
$("overrideForm").addEventListener("submit", clearLock);
$("refreshLockStatusButton").addEventListener("click", refreshLockStatus);
$("resetLockFormButton").addEventListener("click", resetLockForm);
$("connectSerialDeviceButton").addEventListener("click", connectSerialDevice);
$("disconnectSerialDeviceButton").addEventListener("click", disconnectSerialDevice);
$("overrideUserName").addEventListener("input", updateGodReasonVisibility);
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
$("setupMachinesSectionTab").addEventListener("click", async () => {
  showSetupSection("Machines");
  await refreshMachines(state.selectedResourceId);
});
$("setupUsersSectionTab").addEventListener("click", () => showSetupSection("Users"));
$("setupRulesSectionTab").addEventListener("click", () => showSetupSection("Rules"));
$("setupHistorySectionTab").addEventListener("click", () => showSetupSection("History"));
$("setupArchiveSectionTab").addEventListener("click", () => showSetupSection("Archive"));
$("historyLedgerTab").addEventListener("click", () => showHistoryView("Ledger"));
$("historyChartsTab").addEventListener("click", () => showHistoryView("Charts"));
$("historyIssuesTab").addEventListener("click", () => showHistoryView("Issues"));
$("historyExportTab").addEventListener("click", () => showHistoryView("Export"));
$("machineSetupForm").addEventListener("submit", saveMachine);
$("newMachineButton").addEventListener("click", newMachine);
$("deleteSelectedMachineButton").addEventListener("click", deleteSelectedMachine);
$("machineImportForm").addEventListener("submit", importMachinesXlsx);
$("exportMachineTemplateButton").addEventListener("click", exportMachineTemplate);
$("userSetupForm").addEventListener("submit", saveUser);
$("userImportForm").addEventListener("submit", importUsersXlsx);
$("exportUserTemplateButton").addEventListener("click", exportUserTemplate);
$("createManualBackupButton").addEventListener("click", createManualBackup);
$("clearDatabaseButton").addEventListener("click", clearHistoryDataForRestoreTest);
$("restoreLatestBackupButton").addEventListener("click", restoreLatestBackup);
$("archivePreviewForm").addEventListener("submit", previewArchive);
$("createArchiveButton").addEventListener("click", createArchive);
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
$("showGettingStartedButton").addEventListener("click", openGettingStarted);
$("closeGettingStartedButton").addEventListener("click", closeGettingStarted);
$("gettingStartedModal").addEventListener("click", (event) => {
  if (event.target.id === "gettingStartedModal") {
    closeGettingStarted();
  }
});
$("customRuleForm").addEventListener("submit", saveCustomRule);
$("partsImportForm").addEventListener("submit", importPartsXlsx);
$("exportSetupTemplateButton").addEventListener("click", exportSetupTemplate);
$("partReviewFilter").addEventListener("input", () => {
  syncHistoryFiltersFrom("Ledger");
  applyHistoryFilters();
});
$("reviewJobNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Ledger");
  applyHistoryFilters();
});
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
$("reportPartNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Charts");
  applyHistoryFilters();
});
$("reportJobNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Charts");
  applyHistoryFilters();
});
$("reportPartNum").addEventListener("change", refreshReportOperationChoices);
$("reportJobNum").addEventListener("change", refreshReportOperationChoices);
$("topIssuesPartNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Issues");
  applyHistoryFilters();
});
$("topIssuesJobNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Issues");
  applyHistoryFilters();
});
$("topIssuesForm").addEventListener("submit", loadTopIssues);
$("summaryJobNum").addEventListener("input", () => {
  syncHistoryFiltersFrom("Export");
  applyHistoryFilters();
});
$("jobSummaryForm").addEventListener("submit", loadJobSummary);
$("jobSummaryCsvButton").addEventListener("click", openJobSummaryCsv);
$("reportForm").addEventListener("submit", runReport);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");
restoreAuthenticatedSession();
clearInspectionSetupForm();

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}


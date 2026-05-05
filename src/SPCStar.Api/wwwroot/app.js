const state = {
  user: null,
  snapshot: null,
  contexts: [],
  selectedPlans: [],
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

function selectedValues() {
  const set = inspectionSets()[Number($("inspectionSet").value)];
  return {
    jobNum: $("jobNum").value,
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
  fillSelect($("jobNum"), state.snapshot.jobs, (job) => job.jobNum, (job) => job.jobNum);
  fillSelect($("resourceId"), state.snapshot.resources, (resource) => resource.resourceId, (resource) => resource.resourceId);
  const sets = inspectionSets();
  fillSelect(
    $("inspectionSet"),
    sets,
    (_, index) => String(index),
    (set) => `${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.plans.length} variables`);
  if (sets.length && state.snapshot.jobs.length && state.snapshot.resources.length) {
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

async function loadContext(event) {
  event?.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
  if (!set || !jobNum || !resourceId) {
    return;
  }
  state.selectedPlans = set.plans;
  state.contexts = await Promise.all(set.plans.map((plan) => loadVariableContext(jobNum, resourceId, plan)));
  renderContext();
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
  $("contextSubtitle").textContent = `${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.plans.length} variables`;
  renderOverallDueStatus();
  renderLock(state.contexts.find((context) => context.activeLock)?.activeLock);
  renderVariables();
  renderMeasurements();
}

function renderOverallDueStatus() {
  const statuses = state.contexts.map((context) => context.frequencyStatus.status);
  const status = statuses.includes("Overdue") ? "Overdue" : statuses.includes("DueNow") ? "DueNow" : "NotDue";
  const kind = status === "Overdue" ? "danger" : status === "DueNow" ? "warn" : "ok";
  setStatus($("dueStatus"), status, kind);
}

function renderLock(activeLock) {
  const banner = $("lockBanner");
  if (!activeLock) {
    banner.classList.add("hidden");
    banner.textContent = "";
    return;
  }
  banner.classList.remove("hidden");
  banner.textContent = `LOCKED: ${activeLock.ruleTriggered} at ${formatTime(activeLock.lockedAt)}`;
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
        <div class="limit-grid">
          <div><span>LSL</span><strong>${formatNumber(context.lowerSpecLimit)}</strong></div>
          <div><span>Target</span><strong>${formatNumber(plan.nominal)}</strong></div>
          <div><span>USL</span><strong>${formatNumber(context.upperSpecLimit)}</strong></div>
          <div><span>LCL</span><strong>${formatNumber(context.lowerControlLimit)}</strong></div>
          <div><span>Center</span><strong>${formatNumber(plan.nominal)}</strong></div>
          <div><span>UCL</span><strong>${formatNumber(context.upperControlLimit)}</strong></div>
        </div>
      </div>
      <label>
        Measurement
        <input class="measurement-input" data-plan-index="${index}" type="number" step="0.0001" inputmode="decimal" placeholder="0.0000">
      </label>`;
    list.appendChild(card);
  });
}

function renderMeasurements() {
  const list = $("measurementList");
  const rows = state.contexts.flatMap((context) =>
    context.recentMeasurements.map((point) => ({
      characteristicName: context.request.characteristicName,
      ...point
    })));
  if (!rows.length) {
    list.className = "measurement-list empty";
    list.textContent = "No measurements yet.";
    return;
  }
  list.className = "measurement-list";
  list.innerHTML = "";
  rows
    .sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp))
    .slice(0, 12)
    .forEach((point) => {
      const row = document.createElement("div");
      row.className = "measurement-row";
      row.innerHTML = `<span>${formatTime(point.timestamp)} ${point.characteristicName}</span><strong>${formatNumber(point.value)}</strong><span>${point.hasRuleViolation ? "Violation" : "OK"}</span>`;
      list.appendChild(row);
    });
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
          clientRecordId: crypto.randomUUID(),
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

async function loadSetupAdmin() {
  state.roles = await api("/setup/roles");
  state.users = await api("/setup/users");
  fillSelect($("setupRole"), state.roles, (role) => role, (role) => role);
  renderUsers();
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

window.addEventListener("online", () => setStatus($("syncStatus"), "Online", "ok"));
window.addEventListener("offline", () => setStatus($("syncStatus"), "Offline", "warn"));
$("loginForm").addEventListener("submit", login);
$("contextForm").addEventListener("submit", loadContext);
$("measurementForm").addEventListener("submit", submitMeasurement);
$("refreshButton").addEventListener("click", loadContext);
$("inspectionTab").addEventListener("click", () => showPanel("inspect"));
$("setupTab").addEventListener("click", () => showPanel("setup"));
$("userSetupForm").addEventListener("submit", saveUser);
$("inspectionSetupForm").addEventListener("submit", saveInspectionSetup);
$("csvImportForm").addEventListener("submit", importCsv);
$("csvTemplateButton").addEventListener("click", loadCsvTemplate);
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}

const state = {
  user: null,
  snapshot: null,
  contexts: [],
  selectedPlans: []
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
  await loadSnapshot();
}

async function loadSnapshot() {
  state.snapshot = await api("/sync/setup-snapshot");
  $("setupVersion").textContent = `Setup ${state.snapshot.setupVersion}`;
  fillSelect($("jobNum"), state.snapshot.jobs, (job) => job.jobNum, (job) => job.jobNum);
  fillSelect($("resourceId"), state.snapshot.resources, (resource) => resource.resourceId, (resource) => resource.resourceId);
  fillSelect(
    $("inspectionSet"),
    inspectionSets(),
    (_, index) => String(index),
    (set) => `${set.partNum} / ${set.processCode} ${set.operationSeq} / ${set.plans.length} variables`);
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

async function loadContext(event) {
  event?.preventDefault();
  const { jobNum, resourceId, set } = selectedValues();
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
setStatus($("syncStatus"), navigator.onLine ? "Online" : "Offline", navigator.onLine ? "ok" : "warn");

if ("serviceWorker" in navigator) {
  navigator.serviceWorker.register("/service-worker.js").catch(() => {});
}

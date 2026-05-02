const state = {
  user: null,
  snapshot: null,
  context: null,
  selectedPlan: null
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

function selectedValues() {
  const plan = state.snapshot.inspectionPlans[Number($("inspectionPlan").value)];
  return {
    jobNum: $("jobNum").value,
    resourceId: $("resourceId").value,
    plan
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
    $("inspectionPlan"),
    state.snapshot.inspectionPlans,
    (_, index) => String(index),
    (plan) => `${plan.partNum} / ${plan.processCode} ${plan.operationSeq} / ${plan.characteristicName}`);
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
  const { jobNum, resourceId, plan } = selectedValues();
  state.selectedPlan = plan;
  const params = new URLSearchParams({
    jobNum,
    partNum: plan.partNum,
    processCode: plan.processCode,
    operationSeq: String(plan.operationSeq),
    resourceId,
    characteristicName: plan.characteristicName
  });
  state.context = await api(`/work-context?${params}`);
  renderContext();
}

function renderContext() {
  const context = state.context;
  const plan = state.selectedPlan;
  $("contextTitle").textContent = `${context.request.jobNum} ${context.request.resourceId}`;
  $("contextSubtitle").textContent = `${plan.partNum} / ${plan.processCode} ${plan.operationSeq} / ${plan.characteristicName}`;
  $("lsl").textContent = formatNumber(context.lowerSpecLimit);
  $("nominal").textContent = formatNumber(plan.nominal);
  $("usl").textContent = formatNumber(context.upperSpecLimit);
  $("lcl").textContent = formatNumber(context.lowerControlLimit);
  $("center").textContent = formatNumber(context.inspectionPlan?.nominal);
  $("ucl").textContent = formatNumber(context.upperControlLimit);
  renderDueStatus(context.frequencyStatus.status);
  renderLock(context.activeLock);
  renderMeasurements(context.recentMeasurements);
}

function renderDueStatus(status) {
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

function renderMeasurements(points) {
  const list = $("measurementList");
  if (!points.length) {
    list.className = "measurement-list empty";
    list.textContent = "No measurements yet.";
    return;
  }
  list.className = "measurement-list";
  list.innerHTML = "";
  [...points].reverse().forEach((point) => {
    const row = document.createElement("div");
    row.className = "measurement-row";
    row.innerHTML = `<span>${formatTime(point.timestamp)}</span><strong>${formatNumber(point.value)}</strong><span>${point.hasRuleViolation ? "Violation" : "OK"}</span>`;
    list.appendChild(row);
  });
}

async function submitMeasurement(event) {
  event.preventDefault();
  const value = Number($("measurementValue").value);
  if (!Number.isFinite(value)) {
    showEntryMessage("Enter a measurement value.", "error");
    return;
  }
  const { jobNum, resourceId, plan } = selectedValues();
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
        value,
        timestamp: new Date().toISOString(),
        operatorUserId: state.user.userName,
        deviceId: "browser-dev",
        clientRecordId: crypto.randomUUID(),
        submittedAt: new Date().toISOString()
      })
    });
    $("measurementValue").value = "";
    showEntryMessage("Measurement submitted.", "ok");
    await loadContext();
  } catch (error) {
    showEntryMessage("Measurement rejected. " + readableError(error), "error");
  }
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

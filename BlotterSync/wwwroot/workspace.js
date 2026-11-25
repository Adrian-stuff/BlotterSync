"use strict";
const $ = (selector) => document.querySelector(selector);
const icon = (name) =>
  `<svg class="icon" aria-hidden="true"><use href="#i-${name}"/></svg>`;
const esc = (value) =>
  String(value ?? "").replace(
    /[&<>"']/g,
    (c) =>
      ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[
        c
      ],
  );
const state = {
  page: "overview",
  records: [],
  categories: [],
  runs: [],
  me: null,
  agent: null,
  sync: null,
  selected: null,
  run: null,
  busy: false,
  search: "",
  filter: "all",
  intake: { notes: "", narrative: "", location: "", date: "", category: "" },
};
let pendingRequest, toastTimer;
let chat = createChatState();
const pageMeta = {
  overview: [
    "Overview",
    "Incident overview",
    "A clear view of your records and the work ahead.",
  ],
  incidents: [
    "Incident register",
    "Incident register",
    "Find the details. Keep the account clear.",
  ],
  chat: [
    "Desk Chat",
    "Desk Chat",
    "Ask about an incident or get help writing an intake.",
  ],
  intake: [
    "Intake assistant",
    "A clear account starts here",
    "Turn the initial account into a draft you can verify and file.",
  ],
  history: [
    "Agent history",
    "Your agent history",
    "Every draft, source and review decision in one place.",
  ],
  settings: [
    "Connections",
    "Workspace connections",
    "Know where your records and assistance come from.",
  ],
};
function badge(status) {
  const known = [
    "Pending",
    "Ongoing",
    "Resolved",
    "Dismissed",
    "needs_review",
    "accepted",
    "rejected",
    "failed",
    "cancelled",
    "running",
  ];
  return `<span class="badge ${known.includes(status) ? status.toLowerCase() : ""}">${esc(status === "needs_review" ? "Needs review" : status.charAt(0).toUpperCase() + status.slice(1))}</span>`;
}
function when(value, opts = {}) {
  return value
    ? new Intl.DateTimeFormat("en-PH", {
        timeZone: "Asia/Manila",
        month: "short",
        day: "numeric",
        ...opts,
      }).format(new Date(value.endsWith("Z") ? value : value + "Z"))
    : "Not recorded";
}
function category(record) {
  return (
    state.categories.find((c) => c.categoryId === record.categoryId)?.name ||
    "Uncategorized incident"
  );
}
function showError(message) {
  $("#global-error").textContent = message;
  $("#global-error").hidden = false;
}
function clearError() {
  $("#global-error").hidden = true;
}
function toast(message) {
  clearTimeout(toastTimer);
  $("#toast").textContent = message;
  $("#toast").hidden = false;
  toastTimer = setTimeout(() => ($("#toast").hidden = true), 4500);
}
async function api(path, options = {}) {
  const token = localStorage.getItem("jwtToken");
  const response = await fetch(path, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options.headers,
    },
  });
  if (response.status === 401) {
    localStorage.removeItem("jwtToken");
    showAuth();
    throw new Error("Your session has ended. Sign in to continue.");
  }
  if (!response.ok) {
    let detail;
    const text = await response.text();
    try {
      const data = JSON.parse(text);
      detail =
        data.error ||
        data.title ||
        Object.values(data.errors || {})
          .flat()
          .join(" ");
    } catch {
      detail = text;
    }
    throw new Error(
      response.status === 429
        ? "The assistant has reached its request limit. Try again in a minute."
        : detail || `Request failed (${response.status}). Please retry.`,
    );
  }
  return response.status === 204 ? null : response.json();
}
function showAuth() {
  chat.request?.abort();
  chat = createChatState();
  $("#auth").hidden = false;
  $("#shell").hidden = true;
}
async function login(demo = false) {
  const button = demo ? $("#demo-login") : $("#login-form button[type=submit]");
  button.disabled = true;
  $("#login-error").textContent = "";
  try {
    const response = await fetch(demo ? "/api/Auth/Demo" : "/api/Auth/Login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: demo
        ? undefined
        : JSON.stringify({
            username: $("#username").value,
            password: $("#password").value,
          }),
    });
    if (!response.ok)
      throw new Error(
        demo
          ? "The demo is available only when development demo mode is enabled."
          : "Check your username and password and try again.",
      );
    const data = await response.json();
    localStorage.setItem("jwtToken", data.token);
    localStorage.setItem("userRole", data.role);
    $("#password").value = "";
    await boot();
  } catch (error) {
    $("#login-error").textContent = error.message;
  } finally {
    button.disabled = false;
  }
}
async function loadData() {
  const [records, categories, runs, agent, sync] = await Promise.all([
    api("/api/BlotterRecords"),
    api("/api/Categories"),
    api("/api/agent/runs"),
    api("/api/agent/status"),
    api("/api/Sync/Status"),
  ]);
  Object.assign(state, { records, categories, runs, agent, sync });
  $("#nav-count").textContent = records.length;
  const el = $("#cloud-status");
  el.textContent = !sync.enabled
    ? "Local database"
    : sync.cloudOnline
      ? "Cloud connected"
      : "Working locally";
  el.classList.toggle("offline", sync.enabled && !sync.cloudOnline);
}
async function boot() {
  if (!localStorage.getItem("jwtToken")) return showAuth();
  try {
    state.me = await api("/api/Auth/Me");
    clearError();
    $("#profile-name").textContent = state.me.name;
    $("#profile-role").textContent = state.me.role;
    $("#avatar").textContent = state.me.name
      .split(" ")
      .map((x) => x[0])
      .slice(0, 2)
      .join("");
    $("#demo-banner").hidden = !state.me.demo;
    $("#today").textContent = new Intl.DateTimeFormat("en-PH", {
      timeZone: "Asia/Manila",
      month: "short",
      day: "numeric",
      year: "numeric",
    }).format(new Date());
    await loadData();
    $("#auth").hidden = true;
    $("#shell").hidden = false;
    navigate(location.hash === "#chat" ? "chat" : "overview");
  } catch (error) {
    if (localStorage.getItem("jwtToken")) {
      const message = `${error.message} Reload the page to retry.`;
      if ($("#shell").hidden) {
        showAuth();
        $("#login-error").textContent = message;
      } else {
        showError(message);
      }
    }
  }
}
function navigate(page) {
  if (state.busy) {
    toast("Cancel or finish the current draft before changing views.");
    return;
  }
  state.page = page;
  const url = new URL(location.href);
  if (page === "chat") url.hash = "chat";
  else if (url.hash === "#chat") url.hash = "";
  if (url.href !== location.href) window.history.replaceState(null, "", url);
  state.search = "";
  state.filter = "all";
  state.selected = null;
  state.run = null;
  clearError();
  document.querySelectorAll("[data-nav]").forEach((b) => {
    b.classList.toggle("active", b.dataset.nav === page);
    if (b.dataset.nav === page) b.setAttribute("aria-current", "page");
    else b.removeAttribute("aria-current");
  });
  const meta = pageMeta[page];
  $("#breadcrumb").textContent = meta[0];
  $("#page-title").textContent = meta[1];
  $("#page-description").textContent = meta[2];
  $("#new-incident").hidden = page === "intake" || page === "chat";
  $("#sidebar").classList.remove("open");
  $("#menu-toggle").setAttribute("aria-expanded", "false");
  render();
  if (page === "chat") void refreshChatList();
}
function metrics() {
  const pending = state.records.filter((r) => r.status === "Pending").length,
    ongoing = state.records.filter((r) => r.status === "Ongoing").length,
    resolved = state.records.filter((r) => r.status === "Resolved").length;
  return `<section class="metrics" aria-label="Incident totals">${[
    [
      "folder",
      "Total incidents",
      state.records.length,
      "Within your record access",
    ],
    ["clock", "Awaiting attention", pending, "Pending incidents"],
    ["sync", "In progress", ongoing, "Ongoing incidents"],
    ["check", "Resolved", resolved, "Recorded resolutions"],
  ]
    .map(
      ([i, label, value, sub]) =>
        `<div class="metric"><span class="metric-label">${icon(i)}${label}</span><div class="metric-value">${value}</div><small>${sub}</small></div>`,
    )
    .join("")}</section>`;
}
function register() {
  return `<section class="panel"><div class="panel-heading"><div><h2>${state.page === "overview" ? "Recent incidents" : "All incidents"}</h2><p>${state.page === "overview" ? "The latest records at your desk." : "Records available to your officer account."}</p></div>${state.page === "overview" ? '<button class="button quiet" data-action="view-all">View all</button>' : ""}</div><div class="filters"><label class="search">${icon("search")}<input id="record-search" type="search" aria-label="Search incidents" placeholder="Search records…" value="${esc(state.search)}"></label><select id="status-filter" aria-label="Filter by status"><option value="all">All statuses</option>${["Pending", "Ongoing", "Resolved", "Dismissed"].map((s) => `<option ${state.filter === s ? "selected" : ""}>${s}</option>`).join("")}</select></div><div id="incident-rows"></div></section>`;
}
function renderRows() {
  const records = state.records.filter(
    (r) =>
      (state.filter === "all" || r.status === state.filter) &&
      `${r.trackingNumber} ${r.location} ${r.narrative} ${category(r)}`
        .toLowerCase()
        .includes(state.search.toLowerCase()),
  );
  const shown = state.page === "overview" ? records.slice(0, 6) : records;
  $("#incident-rows").innerHTML = shown.length
    ? `<div class="table-wrap"><table><thead><tr><th scope="col">Incident</th><th scope="col" class="location-cell">Location</th><th scope="col">Status</th></tr></thead><tbody>${shown.map((r) => `<tr><td><button class="incident-link" data-record="${r.recordId}">${esc(category(r))}</button><span class="tracking">${esc(r.trackingNumber)} · ${when(r.incidentDate)}</span></td><td class="location-cell">${esc(r.location)}</td><td>${badge(r.status)}</td></tr>`).join("")}</tbody></table></div><div class="table-foot"><span>Showing ${shown.length} of ${records.length} incidents</span><span>Latest first</span></div>`
    : `<div class="empty"><h3>${state.records.length ? "No matching incidents" : "Your register is ready"}</h3>${state.records.length ? "Try another search or choose all statuses." : "Use New incident to record the first account."}</div>`;
}
function assistantStart() {
  return `<div class="assistant-top"><span class="assistant-symbol">${icon("spark")}</span><div><h2>Desk assistant</h2><small>${state.agent?.model ? "Model configured" : "Local assistance"}</small></div></div><h3>Less paperwork.<br>More clarity.</h3><p>Prepare a brief, find the missing details, and keep your next step in view.</p><button class="workflow-option" data-action="chat">${icon("chat")}<span>Open Desk Chat<small>Ask a question and follow up</small></span>${icon("chevron")}</button><button class="workflow-option" data-action="pick-record">${icon("file")}<span>Summarize an incident<small>Start with a record from your register</small></span>${icon("chevron")}</button><button class="workflow-option" data-action="intake">${icon("spark")}<span>Prepare an intake<small>Shape initial notes into a clear account</small></span>${icon("chevron")}</button><div class="assistant-note">${state.agent?.model ? "AI drafts can contain mistakes. Verify each detail before accepting." : "Local mode extracts supplied text. Connect AI in Connections for generated summaries."}</div>`;
}
function queueSummary() {
  return `<section class="panel queue-summary"><h2 class="section-title">Before you close the desk</h2><div class="queue-line"><span>Pending incidents</span><strong>${state.records.filter((r) => r.status === "Pending").length} to review</strong></div><div class="queue-line"><span>Assistant drafts</span><strong>${state.runs.filter((r) => r.status === "needs_review").length} awaiting your review</strong></div><div class="queue-line"><span>Cloud outbox</span><strong>${state.sync?.enabled ? `${state.sync.pendingChanges} waiting to sync` : "Local storage active"}</strong></div></section>`;
}
function intakeForm() {
  return `<section class="panel intake-form"><h2>Start with what was reported</h2><p>Use the original account. Keep uncertain details attributed to the person reporting them.</p><div class="form-field"><label for="intake-notes">Initial account</label><textarea id="intake-notes" rows="7" maxlength="6000" placeholder="What happened, where, when, and who reported it?">${esc(state.intake.notes)}</textarea><small>Up to 6,000 characters. Include only information needed for this record.</small></div><div class="button-row"><button class="button primary" data-action="generate-intake">${icon("spark")}Prepare intake draft</button><span class="tracking">${state.agent?.model ? "AI-assisted · review required" : "Local extraction · no model connected"}</span></div><form id="file-incident" class="manual-fields"><h3>Review and file the incident</h3><p>You can enter these fields directly or use an accepted draft. Filing creates a Pending record.</p><div class="form-grid"><div class="form-field"><label for="incident-category">Incident category</label><select id="incident-category" required><option value="">Choose a category</option>${state.categories.map((c) => `<option value="${c.categoryId}" ${String(c.categoryId) === String(state.intake.category) ? "selected" : ""}>${esc(c.name)}</option>`).join("")}</select></div><div class="form-field"><label for="incident-date">Incident date and time</label><input id="incident-date" type="datetime-local" required value="${esc(state.intake.date)}"><small>Philippine time (UTC+8)</small></div></div><div class="form-field"><label for="incident-location">Exact location</label><input id="incident-location" required maxlength="255" value="${esc(state.intake.location)}" placeholder="Street, purok or a verified landmark"></div><div class="form-field"><label for="incident-narrative">Verified narrative</label><textarea id="incident-narrative" rows="6" required maxlength="12000">${esc(state.intake.narrative)}</textarea></div><label class="confirmation"><input type="checkbox" id="verified" required> I have checked this account and the incident details.</label><p id="file-error" class="error" role="alert"></p><button type="submit" class="button primary">${icon("plus")}File incident</button></form></section>`;
}
function draftHistory() {
  return `<section class="panel"><div class="panel-heading"><div><h2>Recent runs</h2><p>Your last 50 drafts and review decisions.</p></div><button class="button quiet" data-action="refresh-history">Refresh</button></div>${state.runs.length ? `<div class="table-wrap"><table class="history-table"><thead><tr><th scope="col">Draft</th><th scope="col">Status</th><th scope="col">Created</th></tr></thead><tbody>${state.runs.map((r) => `<tr><td><button class="incident-link" data-run="${esc(r.id)}">${esc(r.result?.title || (r.workflow === "intake_draft" ? "Intake draft" : "Incident brief"))}</button><span class="tracking">${r.mode === "local" ? "Local extraction" : "AI generated"}</span></td><td>${badge(r.status)}</td><td class="tracking">${when(r.createdAt, { hour: "2-digit", minute: "2-digit" })}</td></tr>`).join("")}</tbody></table></div>` : '<div class="empty"><h3>Your first draft starts at the desk</h3>Summarize a record or prepare an intake. Its sources and your review decision will appear here.</div>'}</section>`;
}
function settings() {
  const s = state.sync,
    a = state.agent;
  return `<section class="panel settings-body"><div class="connection-row">${icon("spark")}<div><h3>${a.mode === "ollama" ? "Ollama is configured" : a.model ? "OpenAI is configured" : "Local rules are active"}</h3><p>${a.model ? `Desk Chat and drafts use ${esc(a.model)} on ${a.mode === "ollama" ? "your configured Ollama server" : "OpenAI"}. The provider receives the recent conversation and the data its tools read.` : "Desk Chat can answer common questions using local rules. Configure OpenAI or Ollama on the server to enable model replies and generated drafts."}</p><p>Every draft requires review. The assistant can read the selected incident and categories; it cannot change an incident. Runs are limited to four model turns and 60 seconds.</p></div></div><div class="connection-row">${icon("sync")}<div><h3>${s.enabled ? "Supabase synchronization" : "SQLite local database"}</h3><p>${s.enabled ? `Cloud connection: ${s.cloudOnline ? "online" : "offline"}. ${s.pendingChanges} changes are waiting to sync; ${s.failedChanges} changes need administrator attention. Last successful sync: ${s.lastSuccessAt ? when(s.lastSuccessAt, { hour: "2-digit", minute: "2-digit" }) : "not yet recorded"}.` : "Your records are stored on this server. Cloud synchronization is not configured. Enable the PostgreSQL provider and server connection string to use the existing Supabase sync service."}</p><button class="button secondary" data-action="refresh-connections">Refresh connections</button></div></div><div class="connection-row">${icon("shield")}<div><h3>Personal run history</h3><p>Draft outputs, source references, tool activity and review decisions are stored in a separate local audit database. Your history is visible only to your account. Accepting a brief records your review; filing an intake is a separate action.</p><a href="dashboard.html">Open the legacy dashboard</a></div></div></section>`;
}
function render() {
  const p = state.page;
  if (p === "chat") {
    renderChat();
    return;
  }
  $("#page-content").innerHTML =
    (p === "overview" ? metrics() : "") +
    (p === "settings"
      ? settings()
      : `<div class="workspace-grid"><div>${p === "intake" ? intakeForm() : p === "history" ? draftHistory() : register() + (p === "overview" ? queueSummary() : "")}</div><aside id="assistant" class="panel assistant-panel" aria-label="Desk assistant">${assistantStart()}</aside></div>`);
  if (p === "overview" || p === "incidents") renderRows();
  if (p === "intake") {
    const syncDraft = () => {
      state.intake = {
        notes: $("#intake-notes").value,
        narrative: $("#incident-narrative").value,
        location: $("#incident-location").value,
        date: $("#incident-date").value,
        category: $("#incident-category").value,
      };
    };
    $("#page-content")
      .querySelectorAll("input,textarea,select")
      .forEach((e) => e.addEventListener("input", syncDraft));
    $("#file-incident").addEventListener("submit", fileIncident);
  }
  $("#record-search")?.addEventListener("input", (e) => {
    state.search = e.target.value;
    renderRows();
  });
  $("#status-filter")?.addEventListener("change", (e) => {
    state.filter = e.target.value;
    renderRows();
  });
  if (state.run) renderRun();
  else if (state.selected) renderRecord();
}
function renderRecord() {
  const r = state.selected;
  if (!r) return;
  $("#assistant").innerHTML =
    `<div class="assistant-top"><span class="assistant-symbol">${icon("file")}</span><div><h2>Incident detail</h2><small>${esc(r.trackingNumber)}</small></div><button class="icon-button" style="margin-left:auto" data-action="close-detail" aria-label="Close incident detail">${icon("close")}</button></div><div class="detail-title"><h3>${esc(category(r))}</h3>${badge(r.status)}</div><div class="detail-meta">${esc(r.location)}<br>${when(r.incidentDate, { year: "numeric", hour: "2-digit", minute: "2-digit" })} · Philippine time</div><div class="narrative">${esc(r.narrative)}</div><div class="result-block"><button class="button primary full-width" data-action="summarize">${icon("spark")}Prepare incident brief</button><button class="button secondary full-width" data-action="chat-with-record">${icon("chat")}Ask about this incident</button><div class="assistant-note">${state.agent.mode === "openai" ? "The selected record will be sent to OpenAI for this draft." : state.agent.mode === "ollama" ? "The selected record will be sent to your configured Ollama server." : "Local assistance will extract this narrative and provide a verification checklist."}</div></div>`;
}
function selectRecord(id) {
  if (state.busy) return;
  state.selected = state.records.find((r) => r.recordId === Number(id));
  state.run = null;
  renderRecord();
  if (window.innerWidth < 981)
    $("#assistant").scrollIntoView({ behavior: "smooth", block: "start" });
}
function renderRun() {
  const run = state.run,
    panel = $("#assistant");
  if (!panel || !run) return;
  const result = run.result;
  panel.innerHTML = `<div class="assistant-top"><span class="assistant-symbol">${icon("spark")}</span><div><h2>${run.workflow === "intake_draft" ? "Intake draft" : "Incident brief"}</h2><small>${when(run.createdAt, { hour: "2-digit", minute: "2-digit" })}</small></div><button class="icon-button" style="margin-left:auto" data-action="close-detail" aria-label="Close draft">${icon("close")}</button></div>${badge(run.status)}<div class="run-mode">${run.mode === "local" ? "Local extraction · supplied text, with no AI inference." : "AI-generated draft · verify against the source."}</div>${run.error ? `<p class="error" role="alert">${esc(run.error)}</p><button class="button secondary" data-action="retry-run">Retry draft</button>` : ""}${result ? `<h3>${esc(result.title)}</h3><p class="narrative">${esc(result.summary)}</p>${result.sourceRecordIds.map((id) => `<button class="button quiet source-link" data-record="${id}">${icon("file")}Source incident #${id}</button>`).join("")}${run.workflow === "intake_draft" ? `<details class="record-source"><summary>Compare with original intake notes</summary><p class="narrative">${esc(run.sourceNotes ?? "Original notes were not stored for this older run. Compare this draft with your original account before filing.")}</p></details>` : ""}${run.recordId ? `<details class="record-source"><summary>Compare with original narrative</summary><p class="narrative">${esc(state.records.find((r) => r.recordId === run.recordId)?.narrative || "The source is no longer available in your register.")}</p></details>` : ""}<div class="result-block">${result.suggestedCategory ? `<h4>Suggested category</h4><p class="narrative">${esc(result.suggestedCategory)}</p>` : ""}<h4>Details to verify</h4>${result.missingInformation.length ? `<ul>${result.missingInformation.map((x) => `<li>${esc(x)}</li>`).join("")}</ul>` : '<p class="narrative">Review the draft for completeness.</p>'}<h4>Next steps</h4><ul>${result.nextSteps.map((x) => `<li>${esc(x)}</li>`).join("")}</ul>${run.status === "needs_review" ? `<div class="button-row"><button class="button primary" data-review="accepted">${icon("check")}${run.workflow === "intake_draft" ? "Accept and use draft" : "Accept brief"}</button><button class="button secondary" data-review="rejected">Reject draft</button></div><div class="assistant-note">${run.workflow === "intake_draft" ? "Accepting places the draft into the filing form for your final check." : "Accepting records your review decision. The incident is unchanged."}</div>` : `<div class="button-row">${run.status === "accepted" && run.workflow === "intake_draft" ? `<button class="button secondary" data-action="use-intake-draft">${icon("file")}Use in filing form</button>` : ""}</div><div class="assistant-note">${run.reviewedAt ? `Reviewed ${when(run.reviewedAt, { hour: "2-digit", minute: "2-digit" })}. The incident record was not changed.` : ""}</div>`}</div>` : ""}<details class="trace" ${run.status === "failed" ? "open" : ""}><summary>View tool activity · ${run.steps.length} steps</summary><ol>${run.steps.map((s) => `<li><strong>${esc(s.tool.replaceAll("_", " "))}</strong>${esc(s.detail)}</li>`).join("")}</ol></details>`;
}
async function generate(workflow) {
  if (state.busy) return;
  clearError();
  const recordId =
    workflow === "incident_summary" ? state.selected?.recordId : null;
  if (workflow === "incident_summary" && !recordId)
    return toast("Select an incident from the register first.");
  if (workflow === "intake_draft" && !state.intake.notes.trim()) {
    $("#intake-notes").focus();
    return toast("Enter the initial account first.");
  }
  state.busy = true;
  state.run = null;
  pendingRequest = new AbortController();
  $("#assistant").innerHTML =
    `<div class="assistant-top"><span class="assistant-symbol">${icon("spark")}</span><div><h2>Preparing your draft</h2><small>${state.agent.model ? "AI assistance" : "Local extraction"}</small></div></div><div class="loading-state"><div class="loading-bar"></div><p>Reading the selected source, checking categories, and preparing a draft for your review.</p><button class="button secondary" data-action="cancel-run">Cancel request</button></div>`;
  if (window.innerWidth < 981)
    $("#assistant").scrollIntoView({ behavior: "smooth", block: "start" });
  try {
    state.run = await api("/api/agent/runs", {
      method: "POST",
      body: JSON.stringify({
        workflow,
        recordId,
        notes: workflow === "intake_draft" ? state.intake.notes : "",
      }),
      signal: pendingRequest.signal,
    });
    state.runs = await api("/api/agent/runs");
    renderRun();
  } catch (error) {
    if (error.name === "AbortError") {
      toast("Request cancelled. The run will remain in your history.");
      state.run = null;
      state.selected
        ? renderRecord()
        : ($("#assistant").innerHTML = assistantStart());
    } else {
      showError(error.message);
      state.selected
        ? renderRecord()
        : ($("#assistant").innerHTML = assistantStart());
    }
  } finally {
    state.busy = false;
    pendingRequest = null;
  }
}
async function review(decision, button) {
  if (!state.run || state.busy) return;
  button.disabled = true;
  clearError();
  try {
    const run = await api(`/api/agent/runs/${state.run.id}/review`, {
      method: "POST",
      body: JSON.stringify({ decision }),
    });
    state.run = run;
    state.runs = await api("/api/agent/runs");
    if (decision === "accepted" && run.workflow === "intake_draft") {
      useIntakeDraft(run);
      toast(
        "Draft accepted. Check the filing form before creating the incident.",
      );
    } else {
      renderRun();
      toast(
        decision === "accepted"
          ? "Brief accepted. The incident record is unchanged."
          : "Draft rejected. Your decision was recorded.",
      );
    }
  } catch (error) {
    showError(error.message);
    button.disabled = false;
  }
}
function useIntakeDraft(run) {
  if (run?.status !== "accepted" || run.workflow !== "intake_draft") return;
  const sameForm = state.page === "intake" && state.run?.id === run.id;
  state.intake = {
    notes: run.sourceNotes ?? "",
    narrative: run.result.summary,
    location: run.result.location,
    category:
      state.categories.find((c) => c.name === run.result.suggestedCategory)
        ?.categoryId || "",
    date: sameForm ? state.intake.date : "",
  };
  if (state.page !== "intake") navigate("intake");
  state.run = run;
  render();
  $("#incident-category").focus();
}
async function fileIncident(event) {
  event.preventDefault();
  if (state.busy)
    return toast("Finish or cancel the draft request before filing.");
  const button = event.target.querySelector("button[type=submit]");
  button.disabled = true;
  $("#file-error").textContent = "";
  try {
    const d = state.intake;
    if (!d.location.trim() || !d.narrative.trim())
      throw new Error("Enter a verified location and narrative.");
    const record = await api("/api/BlotterRecords", {
      method: "POST",
      body: JSON.stringify({
        categoryId: Number(d.category),
        location: d.location.trim(),
        narrative: d.narrative.trim(),
        incidentDate: new Date(`${d.date}:00+08:00`).toISOString(),
      }),
    });
    state.intake = {
      notes: "",
      narrative: "",
      location: "",
      date: "",
      category: "",
    };
    await loadData();
    navigate("incidents");
    selectRecord(record.recordId);
    toast(`Incident ${record.trackingNumber} filed as Pending.`);
  } catch (error) {
    $("#file-error").textContent = error.message;
    button.disabled = false;
  }
}
function createChatState() {
  return {
    thread: null,
    recent: [],
    recordId: null,
    message: "",
    error: "",
    request: null,
    loading: false,
    recentError: "",
    listVersion: 0,
  };
}

function chatTurn(turn) {
  const mode =
    turn.mode === "ollama"
      ? "Ollama reply"
      : turn.mode === "openai"
        ? "OpenAI reply"
        : "Local rules";
  const citations = turn.sourceRecordIds
    .map((id) => {
      const record = state.records.find((item) => item.recordId === id);
      return record
        ? `<button type="button" class="button quiet" data-action="chat-source" data-source-id="${id}">${icon("file")}${esc(record.trackingNumber)}</button>`
        : `<span class="tracking">Incident #${id}</span>`;
    })
    .join("");
  const steps = turn.steps
    .map((step) => `<li>${esc(step.detail)}</li>`)
    .join("");
  return `<li class="chat-turn">
    <div class="chat-message chat-question"><span class="chat-byline">You</span><p>${esc(turn.question)}</p></div>
    <div class="chat-message chat-answer">
      <span class="chat-byline">${icon("chat")}Desk Chat <small>${mode}</small></span>
      <p>${esc(turn.reply)}</p>
      ${citations ? `<div class="chat-citations" aria-label="Sources">${citations}</div>` : ""}
      <details class="chat-trace"><summary>Tool activity (${turn.steps.length})</summary><ul>${steps}</ul></details>
    </div>
  </li>`;
}

function renderChat({ focus = false, bottom = false } = {}) {
  if (state.page !== "chat" || $("#shell").hidden) return;
  const running = Boolean(chat.request || chat.loading);
  const disabled = running ? "disabled" : "";
  const attached = chat.thread ? chat.thread.recordId : chat.recordId;
  const recordOptions = state.records
    .map(
      (r) =>
        `<option value="${r.recordId}" ${r.recordId === attached ? "selected" : ""}>${esc(r.trackingNumber)} · ${esc(category(r))}</option>`,
    )
    .join("");
  const options = attached
    ? ["Show incident details", "What is the status?", "What should I verify?"]
    : ["List incident categories", "What should I verify during intake?"];
  const messages = chat.thread?.turns.length
    ? `<ol class="chat-messages">${chat.thread.turns.map(chatTurn).join("")}</ol>`
    : "";
  const intro = `<div class="chat-empty">
    <span class="assistant-symbol">${icon("chat")}</span>
    <h2>What are you working on?</h2>
    <p>Choose an incident above to ask about its recorded details.</p>
    <div class="chat-suggestions">${options
      .map(
        (prompt) =>
          `<button type="button" class="button secondary" data-action="chat-prompt" data-prompt="${esc(prompt)}" ${disabled}>${esc(prompt)}</button>`,
      )
      .join("")}</div>
  </div>`;
  const model = state.agent?.model;
  const modeText = model
    ? `${state.agent.mode === "ollama" ? "Ollama" : "OpenAI"} · ${esc(model)} · replies use this conversation and any records read for it`
    : "Local rules · supports common records and intake questions";
  $("#page-content").innerHTML = `<div class="chat-layout">
    <section class="panel chat-panel" aria-label="Desk Chat conversation">
      <div class="chat-toolbar">
        <div class="chat-attachment">
          <label for="chat-record">Incident context</label>
          <select id="chat-record" ${chat.thread ? "disabled" : disabled}>
            <option value="">No incident attached</option>${recordOptions}
          </select>
          ${chat.thread ? "<small>Start a new chat to change the incident.</small>" : ""}
        </div>
        <button type="button" class="button secondary" data-action="chat-new" ${disabled}>${icon("plus")}New chat</button>
      </div>
      <div class="chat-transcript" id="chat-transcript" tabindex="0" aria-label="Chat messages">
        ${chat.loading ? '<p class="chat-progress" role="status">Opening conversation…</p>' : messages || (chat.request ? "" : intro)}
        ${chat.request ? `<div class="chat-message chat-question"><span class="chat-byline">You</span><p>${esc(chat.message)}</p></div><p class="chat-progress" role="status">Preparing a reply…</p>` : ""}
      </div>
      <form id="chat-form" class="chat-composer">
        <label for="chat-message">Message</label>
        <textarea id="chat-message" name="message" rows="3" maxlength="4000" required ${running ? "readonly" : ""} placeholder="Ask Desk Chat…">${esc(chat.message)}</textarea>
        <div class="chat-controls">
          <small>${modeText}</small>
          ${
            chat.request
              ? '<button type="button" class="button secondary" data-action="chat-cancel">Cancel</button>'
              : `<button type="submit" class="button primary" ${disabled}>Send</button>`
          }
        </div>
        ${chat.error ? `<p class="error" role="alert">${esc(chat.error)}</p>` : ""}
      </form>
    </section>
    <aside class="panel chat-history" aria-label="Recent chats">
      <h2>Recent chats</h2>
      <p>Only your account can open these chats.</p>
      ${chat.recentError ? `<p class="error">${esc(chat.recentError)}</p><button class="button secondary" data-action="chat-refresh">Retry</button>` : ""}
      ${
        chat.recent.length
          ? `<ul>${chat.recent
              .map(
                (entry) =>
                  `<li><button type="button" class="chat-thread" data-action="chat-open" data-thread-id="${esc(entry.id)}" ${entry.id === chat.thread?.id ? 'aria-current="true"' : ""} ${disabled}><span>${esc(entry.title)}</span><small>${when(entry.updatedAt, { hour: "2-digit", minute: "2-digit" })}</small></button></li>`,
              )
              .join("")}</ul>`
          : !chat.recentError
            ? "<p>Sent conversations appear here.</p>"
            : ""
      }
    </aside>
  </div>`;
  $("#chat-form").addEventListener("submit", sendChat);
  $("#chat-message").addEventListener("input", (event) => {
    chat.message = event.target.value;
  });
  $("#chat-record").addEventListener("change", (event) => {
    chat.recordId = Number(event.target.value) || null;
    renderChat();
    $("#chat-record").focus();
  });
  if (bottom) {
    const transcript = $("#chat-transcript");
    transcript.scrollTop = transcript.scrollHeight;
  }
  if (focus) $("#chat-message").focus({ preventScroll: true });
}

function resetChat(recordId = null) {
  chat.thread = null;
  chat.recordId = recordId;
  chat.message = "";
  chat.error = "";
}

function renderChatList() {
  if (
    state.page !== "chat" ||
    $("#shell").hidden ||
    chat.request ||
    chat.loading
  )
    return;
  const active = document.activeElement;
  const transcript = $("#chat-transcript");
  const scroll = transcript?.scrollTop ?? 0;
  const hadFocus = active?.id === "chat-message";
  const selection = hadFocus
    ? [active.selectionStart, active.selectionEnd]
    : null;
  renderChat({ focus: hadFocus });
  $("#chat-transcript").scrollTop = scroll;
  if (selection) $("#chat-message").setSelectionRange(...selection);
  else if (active?.id === "chat-record")
    $("#chat-record").focus({ preventScroll: true });
}

async function refreshChatList() {
  const current = chat;
  const version = ++current.listVersion;
  try {
    const recent = await api("/api/agent/chat");
    if (chat !== current || version !== current.listVersion) return;
    current.recent = recent;
    current.recentError = "";
  } catch (error) {
    if (chat !== current || version !== current.listVersion) return;
    current.recentError = error.message;
  }
  renderChatList();
}

async function openChat(id) {
  if (chat.request || chat.loading) return;
  const current = chat;
  current.loading = true;
  current.error = "";
  renderChat();
  try {
    const thread = await api(`/api/agent/chat/${encodeURIComponent(id)}`);
    if (chat !== current) return;
    const retryMessage = current.thread?.id === id ? current.message : "";
    resetChat(thread.recordId);
    current.thread = thread;
    current.message = retryMessage;
  } catch (error) {
    current.error = error.message;
  } finally {
    current.loading = false;
    if (chat === current) renderChat({ focus: true, bottom: true });
  }
}

async function sendChat(event) {
  event.preventDefault();
  if (chat.request || chat.loading) return;
  const current = chat;
  const message = current.message.trim();
  if (!message) {
    $("#chat-message").focus();
    return;
  }
  const request = current.thread
    ? { message, threadId: current.thread.id, version: current.thread.version }
    : { message, recordId: current.recordId };
  current.error = "";
  current.request = new AbortController();
  renderChat({ bottom: true });
  $("#page-content button[data-action='chat-cancel']")?.focus({
    preventScroll: true,
  });
  try {
    const thread = await api("/api/agent/chat", {
      method: "POST",
      signal: current.request.signal,
      body: JSON.stringify(request),
    });
    if (chat !== current) return;
    current.thread = thread;
    current.message = "";
    current.listVersion++;
    const updated = {
      id: thread.id,
      title: thread.title,
      updatedAt: thread.turns.at(-1).at,
    };
    current.recent = [
      updated,
      ...current.recent.filter((entry) => entry.id !== thread.id),
    ].slice(0, 30);
  } catch (error) {
    current.error =
      error.name === "AbortError"
        ? "Reply cancelled. You can send the message again."
        : error.message;
  } finally {
    current.request = null;
    if (chat === current) renderChat({ focus: true, bottom: true });
  }
}

$("#login-form").addEventListener("submit", (e) => {
  e.preventDefault();
  login();
});
$("#demo-login").addEventListener("click", () => login(true));
$("#logout").addEventListener("click", () => {
  pendingRequest?.abort();
  localStorage.removeItem("jwtToken");
  localStorage.removeItem("userRole");
  state.busy = false;
  state.intake = {
    notes: "",
    narrative: "",
    location: "",
    date: "",
    category: "",
  };
  showAuth();
});
$("#menu-toggle").addEventListener("click", () => {
  const open = $("#sidebar").classList.toggle("open");
  $("#menu-toggle").setAttribute("aria-expanded", String(open));
});
$("#new-incident").addEventListener("click", () => navigate("intake"));
document
  .querySelectorAll("[data-nav]")
  .forEach((button) =>
    button.addEventListener("click", () => navigate(button.dataset.nav)),
  );
$("#page-content").addEventListener("click", async (event) => {
  const button = event.target.closest("button");
  if (!button) return;
  if (button.dataset.record) return selectRecord(button.dataset.record);
  if (button.dataset.review) return review(button.dataset.review, button);
  if (button.dataset.run) {
    if (state.busy) return;
    state.run = state.runs.find((r) => r.id === button.dataset.run);
    renderRun();
    if (window.innerWidth < 981)
      $("#assistant").scrollIntoView({ behavior: "smooth" });
    return;
  }
  const action = button.dataset.action;
  if (action === "chat") return navigate("chat");
  if (
    action === "chat-with-record" &&
    state.selected &&
    !chat.request &&
    !chat.loading &&
    !state.busy
  ) {
    const recordId = state.selected.recordId;
    resetChat(recordId);
    return navigate("chat");
  }
  if (action === "chat-new") {
    resetChat();
    renderChat({ focus: true });
    return;
  }
  if (action === "chat-cancel") {
    chat.request?.abort();
    return;
  }
  if (action === "chat-open") {
    await openChat(button.dataset.threadId);
    return;
  }
  if (action === "chat-refresh") {
    await refreshChatList();
    return;
  }
  if (action === "chat-prompt") {
    chat.message = button.dataset.prompt;
    renderChat({ focus: true });
    return;
  }
  if (action === "chat-source") {
    const id = Number(button.dataset.sourceId);
    navigate("incidents");
    selectRecord(id);
    return;
  }
  if (action === "use-intake-draft") {
    useIntakeDraft(state.run);
    toast(
      "Draft restored. Verify the filing fields before creating an incident.",
    );
  }
  if (action === "view-all") navigate("incidents");
  if (action === "intake") navigate("intake");
  if (action === "pick-record") {
    if (state.page !== "overview" && state.page !== "incidents")
      navigate("incidents");
    $("#record-search")?.focus();
    toast("Choose an incident title to prepare its brief.");
  }
  if (action === "close-detail") {
    if (state.busy) return;
    state.run = null;
    state.selected = null;
    $("#assistant").innerHTML = assistantStart();
  }
  if (action === "summarize") await generate("incident_summary");
  if (action === "generate-intake") await generate("intake_draft");
  if (action === "cancel-run") pendingRequest?.abort();
  if (action === "retry-run") {
    const previous = state.run;
    if (previous.workflow === "incident_summary") {
      state.selected = state.records.find(
        (r) => r.recordId === previous.recordId,
      );
      await generate("incident_summary");
    } else {
      navigate("intake");
      toast("Check the intake notes and prepare a new draft.");
    }
  }
  if (action === "refresh-history" || action === "refresh-connections") {
    button.disabled = true;
    try {
      await loadData();
      render();
      toast("Workspace updated.");
    } catch (error) {
      showError(error.message);
      button.disabled = false;
    }
  }
});
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    $("#sidebar").classList.remove("open");
    $("#menu-toggle").setAttribute("aria-expanded", "false");
  }
});
boot();

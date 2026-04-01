const API_BASE_URL =
  (typeof window !== "undefined" && window.APP_CONFIG && window.APP_CONFIG.apiBaseUrl) ||
  "http://localhost:5253";
const MAX_TITLE_LEN = 500;
const MAX_NOTE_LEN = 4000;

function renderAppShell() {
  const app = document.getElementById("app");
  if (!app) {
    throw new Error("Missing #app root element.");
  }

  app.innerHTML = `
    <main class="container py-4">
      <header class="mb-3">
        <h1 class="h3 mb-1">Task Dashboard</h1>
        <p class="text-secondary mb-0">Add in one step. Filter when you need to.</p>
      </header>

      <form id="quickAddForm" class="card p-3 mb-3" autocomplete="off" novalidate>
        <div class="row g-2 align-items-stretch">
          <div class="col-12 col-md">
            <input id="quickTitle" name="title" type="text" maxlength="500" placeholder="What needs doing?" aria-label="Task title" class="form-control" />
          </div>
          <div class="col-12 col-md-3">
            <select id="quickProject" name="projectId" aria-label="Project" class="form-select"></select>
          </div>
          <div class="col-12 col-md-auto">
            <button type="submit" class="btn btn-primary w-100">Add</button>
          </div>
        </div>
        <details class="mt-3">
          <summary>More options</summary>
          <div class="row g-2 mt-1">
            <div class="col-12 col-md-3"><label class="form-label small mb-1">Priority</label><select id="quickPriority" name="priority" class="form-select form-select-sm"><option value="Low">Low</option><option value="Medium" selected>Medium</option><option value="High">High</option></select></div>
            <div class="col-12 col-md-3"><label class="form-label small mb-1">Status</label><select id="quickStatus" name="status" class="form-select form-select-sm"><option value="Todo" selected>Todo</option><option value="InProgress">In progress</option><option value="Done">Done</option></select></div>
            <div class="col-12 col-md-3"><label class="form-label small mb-1">Due</label><input id="quickDue" name="dueDate" type="date" class="form-control form-control-sm" /></div>
            <div class="col-12 col-md-3"><label class="form-label small mb-1">Note</label><input id="quickNote" name="description" type="text" maxlength="4000" placeholder="Optional" class="form-control form-control-sm" /></div>
          </div>
        </details>
      </form>

      <section id="mainPanel" class="card p-3" aria-labelledby="panel-label">
        <h2 id="panel-label" class="visually-hidden">Your tasks</h2>
        <div class="d-flex flex-wrap gap-2 mb-3">
          <button id="focusModeBtn" type="button" class="btn btn-outline-secondary btn-sm" aria-pressed="false">Focus</button>
          <select id="projectFilter" aria-label="Filter by project" class="form-select form-select-sm w-auto"></select>
          <select id="priorityFilter" aria-label="Filter by priority" class="form-select form-select-sm w-auto"></select>
          <select id="statusFilter" aria-label="Filter by status" class="form-select form-select-sm w-auto"></select>
        </div>
        <div id="nextTaskStrip" class="alert alert-primary py-2 px-3 mb-3" hidden></div>
        <div id="message" class="small text-secondary mb-2" role="status" aria-live="polite" aria-atomic="true"></div>
        <div id="taskList" class="d-flex flex-column gap-2"></div>
        <p id="taskCount" class="small text-secondary text-end mb-0 mt-3"></p>
      </section>
    </main>
  `;
}

renderAppShell();

const projectFilter = document.getElementById("projectFilter");
const priorityFilter = document.getElementById("priorityFilter");
const statusFilter = document.getElementById("statusFilter");
const taskList = document.getElementById("taskList");
const taskCount = document.getElementById("taskCount");
const message = document.getElementById("message");
const mainPanel = document.getElementById("mainPanel");

const quickAddForm = document.getElementById("quickAddForm");
const quickTitle = document.getElementById("quickTitle");
const quickProject = document.getElementById("quickProject");
const quickPriority = document.getElementById("quickPriority");
const quickStatus = document.getElementById("quickStatus");
const quickDue = document.getElementById("quickDue");
const quickNote = document.getElementById("quickNote");

const focusModeBtn = document.getElementById("focusModeBtn");
const nextTaskStrip = document.getElementById("nextTaskStrip");

let allTasks = [];
let allProjects = [];
let focusModeEnabled = false;

function escapeHtml(str) {
  if (str == null || str === "") return "";
  const div = document.createElement("div");
  div.textContent = String(str);
  return div.innerHTML;
}

/** @param {"neutral" | "loading" | "error" | "success"} kind */
function setStatus(text, kind = "neutral") {
  message.textContent = text || "";
  message.classList.remove("is-busy", "toast-error", "toast-success");
  message.setAttribute("role", kind === "error" ? "alert" : "status");

  if (kind === "loading") {
    message.classList.add("is-busy");
  }
  if (kind === "error") {
    message.classList.add("toast-error");
  }
  if (kind === "success") {
    message.classList.add("toast-success");
  }
}

function setAppLoading(loading) {
  document.body.classList.toggle("app-loading", loading);
  focusModeBtn.disabled = loading;
}

function setQuickAddBusy(busy) {
  quickAddForm.setAttribute("aria-busy", busy ? "true" : "false");
  const submitBtn = quickAddForm.querySelector('button[type="submit"]');
  const noProjects = allProjects.length === 0;
  submitBtn.disabled = busy || noProjects;
  quickTitle.disabled = busy;
  quickProject.disabled = busy || noProjects;
  quickPriority.disabled = busy;
  quickStatus.disabled = busy;
  quickDue.disabled = busy;
  quickNote.disabled = busy;
}

function setFocusBusy(busy) {
  focusModeBtn.disabled = busy || document.body.classList.contains("app-loading");
}

async function readApiError(response) {
  try {
    const text = await response.text();
    if (!text) {
      return `Request failed (${response.status}).`;
    }
    try {
      const j = JSON.parse(text);
      if (j && typeof j.error === "string") {
        return j.error;
      }
    } catch {
      /* not JSON */
    }
    return `Request failed (${response.status}).`;
  } catch {
    return "Could not read error response.";
  }
}

async function fetchJson(url, options) {
  let response;
  try {
    response = await fetch(url, options);
  } catch (e) {
    if (e instanceof TypeError) {
      throw new Error("Network error. Is the API running and reachable?");
    }
    throw e;
  }
  if (!response.ok) {
    throw new Error(await readApiError(response));
  }
  return response.json();
}

function ensureArray(value, label) {
  if (!Array.isArray(value)) {
    throw new Error(`Invalid response: expected a list of ${label}.`);
  }
  return value;
}

function formatDueDate(value) {
  if (!value) return "—";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

function fillProjectDropdowns(projects) {
  const opts = projects
    .map((p) => `<option value="${p.id}">${escapeHtml(p.name)}</option>`)
    .join("");

  projectFilter.innerHTML = '<option value="">All projects</option>' + opts;

  if (projects.length === 0) {
    quickProject.innerHTML = "";
    quickProject.disabled = true;
    quickAddForm.querySelector('button[type="submit"]').disabled = true;
    return;
  }

  quickProject.disabled = false;
  quickAddForm.querySelector('button[type="submit"]').disabled = false;
  quickProject.innerHTML = opts;
  quickProject.value = String(projects[0].id);
}

function fillFilterDropdowns() {
  priorityFilter.innerHTML = `
    <option value="">All priorities</option>
    <option value="Low">Low</option>
    <option value="Medium">Medium</option>
    <option value="High">High</option>
  `;
  statusFilter.innerHTML = `
    <option value="">All statuses</option>
    <option value="Todo">Todo</option>
    <option value="InProgress">In progress</option>
    <option value="Done">Done</option>
  `;
}

function priorityClass(priority) {
  if (priority === "High") return "priority-high";
  if (priority === "Medium") return "priority-medium";
  return "priority-low";
}

function renderTasks(tasks) {
  taskCount.textContent =
    tasks.length === 0 ? "No tasks" : `${tasks.length} task${tasks.length === 1 ? "" : "s"}`;

  if (tasks.length === 0) {
    taskList.innerHTML =
      '<p class="empty-hint">No tasks match these filters — or your list is empty. Add one above.</p>';
    return;
  }

  taskList.innerHTML = tasks
    .map((task) => {
      const pClass = priorityClass(task.priority);
      const cal = task.dueDate
        ? `<div class="task-actions">
             <button type="button" class="btn-calendar calendar-btn" data-task-id="${task.id}" title="Download .ics file">Calendar</button>
           </div>`
        : "";
      return `
      <article class="task-item">
        <p class="task-title">${escapeHtml(task.title)}</p>
        <div class="task-meta">
          <span class="chip">${escapeHtml(task.projectName || "Project")}</span>
          <span class="chip ${pClass}">${escapeHtml(task.priority)}</span>
          <span class="chip">${escapeHtml(task.status)}</span>
          <span class="chip">${formatDueDate(task.dueDate)}</span>
        </div>
        ${cal}
      </article>`;
    })
    .join("");
}

function renderNextStrip(task) {
  if (!task || task.message) {
    nextTaskStrip.hidden = true;
    nextTaskStrip.innerHTML = "";
    return;
  }

  nextTaskStrip.hidden = false;
  const pClass = priorityClass(task.priority);
  const cal = task.dueDate
    ? `<div class="task-actions">
         <button type="button" class="btn-calendar calendar-btn" data-task-id="${task.id}" title="Download .ics file">Calendar</button>
       </div>`
    : "";

  nextTaskStrip.innerHTML = `
    <div class="next-label">Next up</div>
    <div class="next-title">${escapeHtml(task.title)}</div>
    <div class="task-meta">
      <span class="chip">${escapeHtml(task.projectName || "Project")}</span>
      <span class="chip ${pClass}">${escapeHtml(task.priority)}</span>
      <span class="chip">${escapeHtml(task.status)}</span>
      <span class="chip">${formatDueDate(task.dueDate)}</span>
    </div>
    ${cal}
  `;
}

function applyFilters() {
  const projectId = projectFilter.value;
  const priority = priorityFilter.value;
  const status = statusFilter.value;

  const filtered = allTasks.filter((task) => {
    const okProject = !projectId || String(task.projectId) === projectId;
    const okPriority = !priority || task.priority === priority;
    const okStatus = !status || task.status === status;
    return okProject && okPriority && okStatus;
  });

  renderTasks(filtered);
}

async function withListRefresh(fn) {
  taskList.classList.add("is-refreshing");
  try {
    await fn();
  } finally {
    requestAnimationFrame(() => {
      taskList.classList.remove("is-refreshing");
    });
  }
}

async function loadDashboard() {
  setAppLoading(true);
  setStatus("Loading…", "loading");
  try {
    const [projects, tasks, nextTask] = await Promise.all([
      fetchJson(`${API_BASE_URL}/api/projects`).then((d) => ensureArray(d, "projects")),
      fetchJson(`${API_BASE_URL}/api/tasks/all?focusMode=${focusModeEnabled}`).then((d) => ensureArray(d, "tasks")),
      fetchJson(`${API_BASE_URL}/api/tasks/next`)
    ]);

    allProjects = projects;
    allTasks = tasks;

    fillProjectDropdowns(allProjects);
    fillFilterDropdowns();
    applyFilters();
    renderNextStrip(nextTask);
    setStatus("", "neutral");
  } catch (error) {
    const msg = error instanceof Error ? error.message : "Something went wrong.";
    setStatus(msg, "error");
    allProjects = [];
    allTasks = [];
    fillProjectDropdowns([]);
    applyFilters();
    renderNextStrip(null);
  } finally {
    setAppLoading(false);
    focusModeBtn.disabled = false;
  }
}

async function refreshTasksAndNext() {
  await withListRefresh(async () => {
    const tasks = ensureArray(
      await fetchJson(`${API_BASE_URL}/api/tasks/all?focusMode=${focusModeEnabled}`),
      "tasks"
    );
    const nextTask = await fetchJson(`${API_BASE_URL}/api/tasks/next`);
    allTasks = tasks;
    applyFilters();
    renderNextStrip(nextTask);
  });
}

async function exportTaskToCalendar(taskId) {
  setStatus("Preparing calendar…", "loading");
  try {
    let response;
    try {
      response = await fetch(`${API_BASE_URL}/api/tasks/${taskId}/calendar`);
    } catch (e) {
      if (e instanceof TypeError) {
        throw new Error("Network error. Is the API running?");
      }
      throw e;
    }
    if (!response.ok) {
      throw new Error(await readApiError(response));
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `task-${taskId}.ics`;
    link.click();
    URL.revokeObjectURL(url);
    setStatus("", "neutral");
  } catch (error) {
    const msg = error instanceof Error ? error.message : "Export failed.";
    setStatus(msg, "error");
  }
}

quickAddForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const title = quickTitle.value.trim();
  const projectId = Number(quickProject.value);
  const note = quickNote.value.trim();

  if (!title) {
    setStatus("Enter a task title.", "error");
    quickTitle.focus();
    return;
  }
  if (title.length > MAX_TITLE_LEN) {
    setStatus(`Title must be at most ${MAX_TITLE_LEN} characters.`, "error");
    quickTitle.focus();
    return;
  }
  if (note.length > MAX_NOTE_LEN) {
    setStatus(`Note must be at most ${MAX_NOTE_LEN} characters.`, "error");
    quickNote.focus();
    return;
  }
  if (!projectId || Number.isNaN(projectId)) {
    setStatus("Create a project first (API has no projects).", "error");
    return;
  }

  const payload = {
    title,
    description: note || null,
    priority: quickPriority.value,
    status: quickStatus.value,
    projectId,
    dueDate: quickDue.value ? new Date(quickDue.value).toISOString() : null
  };

  setQuickAddBusy(true);
  setStatus("Adding…", "loading");

  try {
    await fetchJson(`${API_BASE_URL}/api/tasks`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    quickTitle.value = "";
    quickNote.value = "";
    quickDue.value = "";
    quickPriority.value = "Medium";
    quickStatus.value = "Todo";

    await refreshTasksAndNext();
    setStatus("Added.", "success");
    quickTitle.focus();
    window.setTimeout(() => {
      if (message.textContent === "Added.") {
        setStatus("", "neutral");
      }
    }, 2000);
  } catch (error) {
    const msg = error instanceof Error ? error.message : "Could not add task.";
    setStatus(msg, "error");
  } finally {
    setQuickAddBusy(false);
  }
});

projectFilter.addEventListener("change", applyFilters);
priorityFilter.addEventListener("change", applyFilters);
statusFilter.addEventListener("change", applyFilters);

taskList.addEventListener("click", async (event) => {
  const btn = event.target.closest(".calendar-btn");
  if (!btn) return;
  const id = Number(btn.getAttribute("data-task-id"));
  if (id) await exportTaskToCalendar(id);
});

nextTaskStrip.addEventListener("click", async (event) => {
  const btn = event.target.closest(".calendar-btn");
  if (!btn) return;
  const id = Number(btn.getAttribute("data-task-id"));
  if (id) await exportTaskToCalendar(id);
});

focusModeBtn.addEventListener("click", async () => {
  focusModeEnabled = !focusModeEnabled;
  focusModeBtn.setAttribute("aria-pressed", String(focusModeEnabled));
  focusModeBtn.textContent = focusModeEnabled ? "Focused" : "Focus";
  setFocusBusy(true);
  setStatus("Updating…", "loading");
  try {
    await refreshTasksAndNext();
    setStatus("", "neutral");
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Update failed.";
    setStatus(msg, "error");
  } finally {
    setFocusBusy(false);
  }
});

fillFilterDropdowns();
loadDashboard().then(() => {
  if (!document.body.classList.contains("app-loading")) {
    quickTitle.focus();
  }
});

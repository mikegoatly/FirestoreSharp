// ── Database selector ──────────────────────────────────────────────────────

import { state } from '/ui/state.js';
import { esc } from '/ui/api.js';

// Injected by app.js
let _resetNavigation;
export function setResetNavigation(fn) { _resetNavigation = fn; }

function hideSelect(selectEl, labelEl) {
  selectEl.classList.add('hidden');
  labelEl.classList.remove('hidden');
}

function buildProjectOptions() {
  const projects = [...new Set(state.knownDatabases.map(d => d.project))];
  els.metaProjectSelect.innerHTML = projects.map(p =>
    `<option value="${esc(p)}"${p === state.project ? ' selected' : ''}>${esc(p)}</option>`
  ).join('');
}

function buildDatabaseOptions() {
  const databases = state.knownDatabases
    .filter(d => d.project === state.project)
    .map(d => d.database);
  els.metaDatabaseSelect.innerHTML = databases.map(db =>
    `<option value="${esc(db)}"${db === state.database ? ' selected' : ''}>${esc(db)}</option>`
  ).join('');
}

function showProjectSelect() {
  if (state.knownDatabases.length <= 1) return;
  buildProjectOptions();
  els.metaProject.classList.add('hidden');
  els.metaProjectSelect.classList.remove('hidden');
  els.metaProjectSelect.focus();
}

function showDatabaseSelect() {
  const dbsForProject = state.knownDatabases.filter(d => d.project === state.project);
  if (dbsForProject.length <= 1) return;
  buildDatabaseOptions();
  els.metaDatabase.classList.add('hidden');
  els.metaDatabaseSelect.classList.remove('hidden');
  els.metaDatabaseSelect.focus();
}

function commitProjectSelect() {
  const newProject = els.metaProjectSelect.value;
  hideSelect(els.metaProjectSelect, els.metaProject);
  if (newProject !== state.project) {
    state.project = newProject;
    const first = state.knownDatabases.find(d => d.project === newProject);
    if (first) state.database = first.database;
    els.metaProject.textContent = state.project;
    els.metaDatabase.textContent = state.database;
    _resetNavigation();
  }
}

function commitDatabaseSelect() {
  const newDatabase = els.metaDatabaseSelect.value;
  hideSelect(els.metaDatabaseSelect, els.metaDatabase);
  if (newDatabase !== state.database) {
    state.database = newDatabase;
    els.metaDatabase.textContent = state.database;
    _resetNavigation();
  }
}

export function initSelector() {
  els.metaProject.addEventListener('click', showProjectSelect);
  els.metaProjectSelect.addEventListener('change', commitProjectSelect);
  els.metaProjectSelect.addEventListener('blur', () => hideSelect(els.metaProjectSelect, els.metaProject));

  els.metaDatabase.addEventListener('click', showDatabaseSelect);
  els.metaDatabaseSelect.addEventListener('change', commitDatabaseSelect);
  els.metaDatabaseSelect.addEventListener('blur', () => hideSelect(els.metaDatabaseSelect, els.metaDatabase));
}

export function updateSelectorState() {
  const multipleProjects = new Set(state.knownDatabases.map(d => d.project)).size > 1;
  const multipleDatabases = state.knownDatabases.filter(d => d.project === state.project).length > 1;
  els.metaProject.classList.toggle('meta-selectable', multipleProjects);
  els.metaDatabase.classList.toggle('meta-selectable', multipleDatabases);
}

// ── DOM refs (selector-scoped) ─────────────────────────────────────────────

const els = {
  metaProject:        document.getElementById('meta-project'),
  metaDatabase:       document.getElementById('meta-database'),
  metaProjectSelect:  document.getElementById('meta-project-select'),
  metaDatabaseSelect: document.getElementById('meta-database-select'),
};

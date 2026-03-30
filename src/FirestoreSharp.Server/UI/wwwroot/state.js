// ── State ──────────────────────────────────────────────────────────────────

export const state = {
  project: 'local',
  database: '(default)',
  knownDatabases: [],  // array of { project, database }

  // Navigation stack: array of { type: 'collection'|'document', id, resourceName }
  navStack: [],

  activeCollection: null,   // collection ID currently shown in middle panel
  activeDocument: null,     // full resourceName of document shown in editor
  editorMode: null,         // 'view' | 'edit' | 'create'

  // Pagination
  docPageToken: null,
  collPageToken: null,
};

export function docsBase() {
  return `projects/${state.project}/databases/${state.database}/documents`;
}

export function currentParent() {
  const base = docsBase();
  if (state.navStack.length === 0) return base;
  return state.navStack[state.navStack.length - 1].resourceName;
}

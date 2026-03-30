// ── Nav entry types & factories ────────────────────────────────────────────

export const NAV = {
  COLLECTION: 'collection',
  DOCUMENT: 'document',
};

export function navCollection(id, parentForDocs) {
  return { type: NAV.COLLECTION, id, resourceName: `${parentForDocs}/${id}`, parentForDocs };
}

export function navDocument(id, resourceName) {
  return { type: NAV.DOCUMENT, id, resourceName, parentForDocs: resourceName };
}

// ── State ──────────────────────────────────────────────────────────────────

export const state = {
  project: 'local',
  database: '(default)',
  knownDatabases: [],  // array of { project, database }

  // Navigation stack: array of nav entries created by navCollection/navDocument
  navStack: [],

  activeCollection: null,   // collection ID currently shown in middle panel
  activeDocument: null,     // full resourceName of document shown in editor
  editorMode: null,         // 'view' | 'edit' | 'create'

  // Pagination
  docPageToken: null,
  collPageToken: null,

  // Parent resource name that the collections panel is currently showing.
  // Tracked separately because the panel can be repopulated by subcollection
  // loads while the nav stack points somewhere deeper.
  collectionsPanelParent: null,
};

export function docsBase() {
  return `projects/${state.project}/databases/${state.database}/documents`;
}

export function currentParent() {
  const base = docsBase();
  if (state.navStack.length === 0) return base;
  return state.navStack[state.navStack.length - 1].resourceName;
}

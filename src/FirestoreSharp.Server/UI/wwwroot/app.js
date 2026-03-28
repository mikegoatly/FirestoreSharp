// FirestoreSharp Emulator UI
// Vanilla JS, no dependencies.

const API = '/api/ui';

// ── State ──────────────────────────────────────────────────────────────────

const state = {
  project: 'local',
  database: '(default)',

  // Navigation stack: array of { type: 'collection'|'document', id, resourceName }
  navStack: [],

  activeCollection: null,   // collection ID currently shown in middle panel
  activeDocument: null,     // full resourceName of document shown in editor
  editorMode: null,         // 'view' | 'edit' | 'create'

  // Pagination
  docPageToken: null,
  collPageToken: null,
};

// ── DOM refs ───────────────────────────────────────────────────────────────

const $ = id => document.getElementById(id);

const els = {
  metaProject: $('meta-project'),
  metaDatabase: $('meta-database'),
  breadcrumb: $('breadcrumb'),

  collectionsList: $('collections-list'),
  btnNewCollection: $('btn-new-collection'),

  documentsPanel: $('documents-panel'),
  documentsPanelTitle: $('documents-panel-title'),
  documentsList: $('documents-list'),
  btnNewDocument: $('btn-new-document'),

  editorPanel: $('editor-panel'),
  editorTitle: $('editor-title'),
  editorView: $('editor-view'),
  editorEdit: $('editor-edit'),
  editorTextarea: $('editor-textarea'),
  editorError: $('editor-error'),
  btnEditDoc: $('btn-edit-doc'),
  btnDeleteDoc: $('btn-delete-doc'),
  btnSaveDoc: $('btn-save-doc'),
  btnCancelEdit: $('btn-cancel-edit'),
  btnCloseEditor: $('btn-close-editor'),

  modalNewCollection: $('modal-new-collection'),
  newCollectionId: $('new-collection-id'),
  newCollectionError: $('new-collection-error'),
  btnCancelCollection: $('btn-cancel-collection'),
  btnConfirmCollection: $('btn-confirm-collection'),
};

// ── Helpers ────────────────────────────────────────────────────────────────

function docsBase() {
  return `projects/${state.project}/databases/${state.database}/documents`;
}

function currentParent() {
  // Build the parent resource name from the nav stack
  const base = docsBase();
  if (state.navStack.length === 0) return base;
  return state.navStack[state.navStack.length - 1].resourceName;
}

async function apiFetch(url, options = {}) {
  const res = await fetch(url, options);
  if (!res.ok) {
    let msg = `HTTP ${res.status}`;
    try { const j = await res.json(); msg = j.detail || j.title || msg; } catch {}
    throw new Error(msg);
  }
  if (res.status === 204) return null;
  return res.json();
}

function esc(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;');
}

// ── Value rendering ────────────────────────────────────────────────────────

function renderValue(uiVal, depth = 0) {
  if (!uiVal) return '<span class="val val-null">null</span>';
  const { type, value } = uiVal;
  switch (type) {
    case 'null':
      return '<span class="val val-null">null</span>';
    case 'bool':
      return `<span class="val val-bool">${value}</span>`;
    case 'int':
      return `<span class="val val-int">${esc(value)}</span>`;
    case 'double':
      return `<span class="val val-double">${esc(value)}</span>`;
    case 'string':
      return `<span class="val val-string">"${esc(value)}"</span>`;
    case 'timestamp':
      return `<span class="val val-timestamp">${esc(value)}</span>`;
    case 'bytes':
      return `<span class="val val-bytes">bytes(${esc(value).slice(0, 20)}…)</span>`;
    case 'reference':
      return `<span class="val val-reference">${esc(value)}</span>`;
    case 'geopoint': {
      const g = value || {};
      return `<span class="val val-geopoint">LatLng(${g.latitude ?? '?'}, ${g.longitude ?? '?'})</span>`;
    }
    case 'array': {
      const items = Array.isArray(value) ? value : [];
      if (depth > 1 || items.length === 0)
        return `<span class="val val-array">[${items.length} items]</span>`;
      const inner = items.slice(0, 5).map(v => renderValue(v, depth + 1)).join(', ');
      const more = items.length > 5 ? `, …+${items.length - 5}` : '';
      return `<span class="val val-array">[${inner}${more}]</span>`;
    }
    case 'map': {
      const keys = value ? Object.keys(value) : [];
      if (depth > 1 || keys.length === 0)
        return `<span class="val val-map">{${keys.length} keys}</span>`;
      const inner = keys.slice(0, 3).map(k =>
        `<span class="field-key">${esc(k)}</span> ${renderValue(value[k], depth + 1)}`
      ).join(', ');
      const more = keys.length > 3 ? `, …+${keys.length - 3}` : '';
      return `<span class="val val-map">{${inner}${more}}</span>`;
    }
    default:
      return `<span class="val">${esc(JSON.stringify(value))}</span>`;
  }
}

// ── Breadcrumb ─────────────────────────────────────────────────────────────

function renderBreadcrumb() {
  let html = `<span class="breadcrumb-item${state.navStack.length === 0 ? ' active' : ''}" data-index="-1">Root</span>`;
  state.navStack.forEach((item, i) => {
    const isLast = i === state.navStack.length - 1;
    html += `<span class="breadcrumb-sep">›</span>`;
    html += `<span class="breadcrumb-item${isLast ? ' active' : ''}" data-index="${i}">${esc(item.id)}</span>`;
  });
  els.breadcrumb.innerHTML = html;

  els.breadcrumb.querySelectorAll('.breadcrumb-item:not(.active)').forEach(el => {
    el.addEventListener('click', () => {
      const idx = parseInt(el.dataset.index, 10);
      if (idx === -1) {
        // Go to root
        state.navStack = [];
        state.activeCollection = null;
        state.activeDocument = null;
        closeEditor();
        renderBreadcrumb();
        loadCollections();
        clearDocuments();
      } else {
        // Trim stack to this index
        const item = state.navStack[idx];
        state.navStack = state.navStack.slice(0, idx + 1);
        state.activeDocument = null;
        closeEditor();
        renderBreadcrumb();

        if (item.type === 'document') {
          // Reload collections for this document's subcollections
          loadCollections();
          clearDocuments();
        } else {
          // Collection — reload its documents
          loadCollections();
          state.activeCollection = item.id;
          loadDocuments(item.id);
        }
      }
    });
  });
}

// ── Collections panel ──────────────────────────────────────────────────────

async function loadCollections(pageToken = null) {
  const parent = currentParent();
  try {
    const params = new URLSearchParams({ parent });
    if (pageToken) params.set('pageToken', pageToken);
    const data = await apiFetch(`${API}/collections?${params}`);

    if (pageToken) {
      // Append
      const existing = els.collectionsList.querySelector('.collection-items');
      if (existing) renderCollectionItems(data, existing, true);
    } else {
      renderCollections(data);
    }
  } catch (e) {
    els.collectionsList.innerHTML = `<div class="empty-state" style="color:var(--danger)">${esc(e.message)}</div>`;
  }
}

function renderCollections(data) {
  if (!data.collectionIds || data.collectionIds.length === 0) {
    els.collectionsList.innerHTML = '<div class="empty-state">No collections yet.</div>';
    state.collPageToken = null;
    return;
  }

  const container = document.createElement('div');
  container.className = 'collection-items';
  els.collectionsList.innerHTML = '';
  els.collectionsList.appendChild(container);
  renderCollectionItems(data, container, false);
}

function renderCollectionItems(data, container, append) {
  if (!append) container.innerHTML = '';

  data.collectionIds.forEach(id => {
    const el = document.createElement('div');
    el.className = 'collection-item';
    if (id === state.activeCollection) el.classList.add('active');
    el.innerHTML = `<span class="coll-icon">◉</span><span class="coll-name">${esc(id)}</span>`;
    el.addEventListener('click', () => selectCollection(id));
    container.appendChild(el);
  });

  // Remove existing load-more
  const existing = els.collectionsList.querySelector('.load-more');
  if (existing) existing.remove();

  if (data.nextPageToken) {
    state.collPageToken = data.nextPageToken;
    const more = document.createElement('div');
    more.className = 'load-more';
    more.innerHTML = `<button class="btn-load-more">Load more collections</button>`;
    more.querySelector('button').addEventListener('click', () => loadCollections(state.collPageToken));
    els.collectionsList.appendChild(more);
  } else {
    state.collPageToken = null;
  }
}

function selectCollection(collectionId) {
  state.activeCollection = collectionId;
  state.activeDocument = null;

  // Update active state in collections panel
  els.collectionsList.querySelectorAll('.collection-item').forEach(el => {
    el.classList.toggle('active', el.querySelector('.coll-name')?.textContent === collectionId);
  });

  // Push to nav stack if not already last item
  const last = state.navStack[state.navStack.length - 1];
  if (!last || last.type !== 'collection' || last.id !== collectionId) {
    // Remove any trailing document from stack and add this collection
    if (last && last.type === 'document') {
      state.navStack.pop();
    }
    const parent = currentParent();
    state.navStack.push({
      type: 'collection',
      id: collectionId,
      resourceName: `${parent}/${collectionId}`  // not a valid resource name but used for sub-collection parent
    });
    // Recalculate — the collection's "parent" for documents is the current parent before push
    state.navStack[state.navStack.length - 1].parentForDocs = parent;
  }

  renderBreadcrumb();
  closeEditor();
  loadDocuments(collectionId);
}

// ── Documents panel ────────────────────────────────────────────────────────

function clearDocuments() {
  els.documentsPanelTitle.textContent = 'Select a collection';
  els.documentsList.innerHTML = '<div class="empty-state">Select a collection to view documents.</div>';
  els.btnNewDocument.classList.add('hidden');
  state.docPageToken = null;
}

async function loadDocuments(collectionId, pageToken = null) {
  // The parent for listing documents is the nav stack BEFORE the collection entry
  const collEntry = state.navStack[state.navStack.length - 1];
  const parent = collEntry?.parentForDocs ?? docsBase();

  els.documentsPanelTitle.textContent = collectionId;
  els.btnNewDocument.classList.remove('hidden');

  if (!pageToken) {
    els.documentsList.innerHTML = '<div class="empty-state">Loading…</div>';
  }

  try {
    const params = new URLSearchParams({ parent, collectionId });
    if (pageToken) params.set('pageToken', pageToken);
    const data = await apiFetch(`${API}/documents?${params}`);

    if (pageToken) {
      const existing = els.documentsList.querySelector('.document-items');
      if (existing) renderDocumentItems(data, collectionId, existing, true);
    } else {
      renderDocuments(data, collectionId);
    }
  } catch (e) {
    els.documentsList.innerHTML = `<div class="empty-state" style="color:var(--danger)">${esc(e.message)}</div>`;
  }
}

function renderDocuments(data, collectionId) {
  if (!data.documents || data.documents.length === 0) {
    els.documentsList.innerHTML = '<div class="empty-state">No documents in this collection.</div>';
    state.docPageToken = null;
    return;
  }

  const container = document.createElement('div');
  container.className = 'document-items';
  els.documentsList.innerHTML = '';
  els.documentsList.appendChild(container);
  renderDocumentItems(data, collectionId, container, false);
}

function renderDocumentItems(data, collectionId, container, append) {
  if (!append) container.innerHTML = '';

  data.documents.forEach(doc => {
    const el = document.createElement('div');
    el.className = 'document-item';
    if (doc.resourceName === state.activeDocument) el.classList.add('active');

    const fieldKeys = Object.keys(doc.fields || {});
    const previewFields = fieldKeys.slice(0, 3).map(k =>
      `<div class="doc-field"><span class="field-key">${esc(k)}</span>${renderValue(doc.fields[k])}</div>`
    ).join('');
    const more = fieldKeys.length > 3 ? `<div class="doc-field" style="color:var(--text-muted)">+${fieldKeys.length - 3} more fields</div>` : '';

    el.innerHTML = `
      <div class="doc-id" title="${esc(doc.resourceName)}">${esc(doc.documentId)}</div>
      <div class="doc-preview">${previewFields}${more}</div>
    `;

    el.addEventListener('click', () => openDocument(doc.resourceName, collectionId));
    container.appendChild(el);
  });

  // Remove existing load-more
  const existing = els.documentsList.querySelector('.load-more');
  if (existing) existing.remove();

  if (data.nextPageToken) {
    state.docPageToken = data.nextPageToken;
    const more = document.createElement('div');
    more.className = 'load-more';
    more.innerHTML = `<button class="btn-load-more">Load more documents</button>`;
    more.querySelector('button').addEventListener('click', () => loadDocuments(collectionId, state.docPageToken));
    els.documentsList.appendChild(more);
  } else {
    state.docPageToken = null;
  }
}

// ── Document editor ────────────────────────────────────────────────────────

async function openDocument(resourceName, collectionId) {
  state.activeDocument = resourceName;

  // Mark active in list
  els.documentsList.querySelectorAll('.document-item').forEach(el => {
    const docId = el.querySelector('.doc-id');
    // Match by checking the title attribute on doc-id
    el.classList.toggle('active', el.querySelector('.doc-id')?.title === resourceName);
  });

  try {
    const data = await apiFetch(`${API}/document?resourceName=${encodeURIComponent(resourceName)}`);
    showEditorView(data);

    // Push document to nav stack and load its subcollections
    const collEntry = state.navStack[state.navStack.length - 1];
    if (!collEntry || collEntry.type !== 'document' || collEntry.id !== data.documentId) {
      // Remove trailing collection entry if it matches current, then add document
      if (collEntry && collEntry.type === 'collection') {
        // keep collection in stack — document goes after
      }
      // Remove any previous document entry
      if (state.navStack[state.navStack.length - 1]?.type === 'document') {
        state.navStack.pop();
      }
      state.navStack.push({
        type: 'document',
        id: data.documentId,
        resourceName: resourceName,
        parentForDocs: resourceName,  // subcollections are listed under document resourceName
      });
    }

    renderBreadcrumb();

    // Load subcollections for this document
    loadSubcollections(resourceName);
  } catch (e) {
    showEditorError(e.message);
  }
}

async function loadSubcollections(docResourceName) {
  try {
    const params = new URLSearchParams({ parent: docResourceName });
    const data = await apiFetch(`${API}/collections?${params}`);
    renderCollections(data);
  } catch {
    // Ignore subcollection load errors silently
  }
}

function showEditorView(doc) {
  state.editorMode = 'view';
  els.editorPanel.classList.remove('hidden');
  els.editorTitle.textContent = doc.documentId;

  // Buttons
  els.btnEditDoc.classList.remove('hidden');
  els.btnDeleteDoc.classList.remove('hidden');
  els.btnSaveDoc.classList.add('hidden');
  els.btnCancelEdit.classList.add('hidden');

  // View mode
  els.editorView.classList.remove('hidden');
  els.editorEdit.classList.add('hidden');

  // Render metadata
  const fields = doc.fields || {};
  const fieldKeys = Object.keys(fields);

  let html = `<div class="doc-meta">
    <div>Created: ${doc.createTime ? new Date(doc.createTime).toLocaleString() : '—'}</div>
    <div>Updated: ${doc.updateTime ? new Date(doc.updateTime).toLocaleString() : '—'}</div>
    <div style="margin-top:4px;word-break:break-all;color:var(--text-muted);font-size:11px">${esc(doc.resourceName)}</div>
  </div>`;

  if (fieldKeys.length === 0) {
    html += '<div class="empty-state">No fields.</div>';
  } else {
    fieldKeys.forEach(k => {
      html += `<div class="editor-field">
        <div class="editor-field-key">${esc(k)}</div>
        <div class="editor-field-value">${renderValue(fields[k])}</div>
      </div>`;
    });
  }

  els.editorView.innerHTML = html;

  // Store doc on element for edit mode
  els.editorPanel.dataset.doc = JSON.stringify(doc);
}

function enterEditMode() {
  const doc = JSON.parse(els.editorPanel.dataset.doc || '{}');
  state.editorMode = 'edit';

  els.editorView.classList.add('hidden');
  els.editorEdit.classList.remove('hidden');
  els.btnEditDoc.classList.add('hidden');
  els.btnDeleteDoc.classList.add('hidden');
  els.btnSaveDoc.classList.remove('hidden');
  els.btnCancelEdit.classList.remove('hidden');

  // Populate textarea with current fields
  els.editorTextarea.value = JSON.stringify(doc.fields || {}, null, 2);
  hideEditorError();
  els.editorTextarea.focus();
}

function showCreateMode(collectionId) {
  state.editorMode = 'create';
  state.activeDocument = null;

  // Deselect document in list
  els.documentsList.querySelectorAll('.document-item').forEach(el => el.classList.remove('active'));

  els.editorPanel.classList.remove('hidden');
  els.editorTitle.textContent = 'New document';
  els.editorView.classList.add('hidden');
  els.editorEdit.classList.remove('hidden');

  els.btnEditDoc.classList.add('hidden');
  els.btnDeleteDoc.classList.add('hidden');
  els.btnSaveDoc.classList.remove('hidden');
  els.btnCancelEdit.classList.remove('hidden');

  // Store creation context
  els.editorPanel.dataset.createCollection = collectionId;
  els.editorPanel.dataset.doc = '';

  els.editorTextarea.value = JSON.stringify({
    fieldName: { type: 'string', value: 'example' }
  }, null, 2);
  hideEditorError();
  els.editorTextarea.focus();
}

function closeEditor() {
  els.editorPanel.classList.add('hidden');
  state.editorMode = null;
  state.activeDocument = null;
  els.documentsList.querySelectorAll('.document-item').forEach(el => el.classList.remove('active'));
}

async function saveDocument() {
  let fields;
  try {
    fields = JSON.parse(els.editorTextarea.value);
  } catch (e) {
    showEditorError('Invalid JSON: ' + e.message);
    return;
  }

  if (typeof fields !== 'object' || Array.isArray(fields)) {
    showEditorError('Fields must be a JSON object.');
    return;
  }

  try {
    if (state.editorMode === 'create') {
      const collectionId = els.editorPanel.dataset.createCollection;
      const collEntry = state.navStack.find(n => n.type === 'collection' && n.id === collectionId);
      const parent = collEntry?.parentForDocs ?? docsBase();

      const body = JSON.stringify({ documentId: null, fields });
      const data = await apiFetch(
        `${API}/document?parent=${encodeURIComponent(parent)}&collectionId=${encodeURIComponent(collectionId)}`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body }
      );
      showEditorView(data);
      state.activeDocument = data.resourceName;
      loadDocuments(collectionId);
    } else {
      const resourceName = state.activeDocument;
      const body = JSON.stringify({ fields, updateMask: null });
      const data = await apiFetch(
        `${API}/document?resourceName=${encodeURIComponent(resourceName)}`,
        { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body }
      );
      showEditorView(data);
      // Refresh the document list entry
      const collEntry = state.navStack.find(n => n.type === 'collection');
      if (collEntry) loadDocuments(collEntry.id);
    }
  } catch (e) {
    showEditorError(e.message);
  }
}

async function deleteDocument() {
  const resourceName = state.activeDocument;
  if (!resourceName) return;
  if (!confirm(`Delete document "${resourceName.split('/').pop()}"? This cannot be undone.`)) return;

  try {
    await apiFetch(`${API}/document?resourceName=${encodeURIComponent(resourceName)}`, { method: 'DELETE' });

    // Remove from nav stack if it's the last entry
    if (state.navStack[state.navStack.length - 1]?.resourceName === resourceName) {
      state.navStack.pop();
    }

    closeEditor();
    renderBreadcrumb();

    // Reload document list and collections (in case subcollections changed)
    const collEntry = state.navStack[state.navStack.length - 1];
    if (collEntry && collEntry.type === 'collection') {
      loadDocuments(collEntry.id);
      loadCollections();
    } else {
      loadCollections();
      clearDocuments();
    }
  } catch (e) {
    alert('Delete failed: ' + e.message);
  }
}

function cancelEdit() {
  if (state.editorMode === 'create') {
    closeEditor();
  } else {
    // Back to view mode
    const doc = JSON.parse(els.editorPanel.dataset.doc || '{}');
    showEditorView(doc);
  }
}

function showEditorError(msg) {
  els.editorError.textContent = msg;
  els.editorError.classList.remove('hidden');
}

function hideEditorError() {
  els.editorError.classList.add('hidden');
}

// ── New collection modal ───────────────────────────────────────────────────

function showNewCollectionModal() {
  els.newCollectionId.value = '';
  els.newCollectionError.textContent = '';
  els.newCollectionError.classList.add('hidden');
  els.modalNewCollection.classList.remove('hidden');
  els.newCollectionId.focus();
}

function hideNewCollectionModal() {
  els.modalNewCollection.classList.add('hidden');
}

async function confirmNewCollection() {
  const id = els.newCollectionId.value.trim();
  if (!id) {
    els.newCollectionError.textContent = 'Collection ID cannot be empty.';
    els.newCollectionError.classList.remove('hidden');
    return;
  }
  if (/[\/.]/.test(id)) {
    els.newCollectionError.textContent = 'Collection ID cannot contain "/" or ".".';
    els.newCollectionError.classList.remove('hidden');
    return;
  }

  hideNewCollectionModal();

  // Navigate into the new collection and open create-document mode
  // First push it onto the nav stack
  const parent = currentParent();
  state.navStack.push({
    type: 'collection',
    id: id,
    resourceName: `${parent}/${id}`,
    parentForDocs: parent,
  });
  state.activeCollection = id;
  renderBreadcrumb();

  // Reload collections panel (may be empty) and open create doc
  renderCollections({ collectionIds: [], nextPageToken: null });
  els.documentsPanelTitle.textContent = id;
  els.btnNewDocument.classList.remove('hidden');
  els.documentsList.innerHTML = '<div class="empty-state">No documents yet. Create the first one.</div>';

  showCreateMode(id);
}

// ── Event listeners ────────────────────────────────────────────────────────

els.btnNewCollection.addEventListener('click', showNewCollectionModal);
els.btnCancelCollection.addEventListener('click', hideNewCollectionModal);
els.btnConfirmCollection.addEventListener('click', confirmNewCollection);
els.newCollectionId.addEventListener('keydown', e => { if (e.key === 'Enter') confirmNewCollection(); });
els.modalNewCollection.querySelector('.modal-backdrop').addEventListener('click', hideNewCollectionModal);

els.btnNewDocument.addEventListener('click', () => {
  const collEntry = state.navStack[state.navStack.length - 1];
  if (collEntry && collEntry.type === 'collection') {
    showCreateMode(collEntry.id);
  }
});

els.btnEditDoc.addEventListener('click', enterEditMode);
els.btnDeleteDoc.addEventListener('click', deleteDocument);
els.btnSaveDoc.addEventListener('click', saveDocument);
els.btnCancelEdit.addEventListener('click', cancelEdit);
els.btnCloseEditor.addEventListener('click', () => {
  if (state.editorMode === 'view') {
    closeEditor();
  } else {
    cancelEdit();
  }
});

// ── Init ───────────────────────────────────────────────────────────────────

async function init() {
  try {
    const config = await apiFetch(`${API}/config`);
    state.project = config.project;
    state.database = config.database;
    els.metaProject.textContent = config.project;
    els.metaDatabase.textContent = config.database;
  } catch {
    els.metaProject.textContent = 'local';
    els.metaDatabase.textContent = '(default)';
  }

  renderBreadcrumb();
  loadCollections();
}

init();

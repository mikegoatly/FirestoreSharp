// ── Collections & documents panels ────────────────────────────────────────

import { state, docsBase, currentParent } from '/ui/state.js';
import { API, apiFetch, esc } from '/ui/api.js';
import { renderValue } from '/ui/render.js';

// Injected by app.js to avoid circular dependencies
let _closeEditor;
let _renderBreadcrumb;
let _showCreateMode;
export function setCallbacks(closeEditor, renderBreadcrumb, showCreateMode) {
  _closeEditor = closeEditor;
  _renderBreadcrumb = renderBreadcrumb;
  _showCreateMode = showCreateMode;
}

// ── Collections panel ──────────────────────────────────────────────────────

export async function loadCollections(pageToken = null) {
  const parent = currentParent();
  try {
    const params = new URLSearchParams({ parent });
    if (pageToken) params.set('pageToken', pageToken);
    const data = await apiFetch(`${API}/collections?${params}`);

    if (pageToken) {
      const existing = els.collectionsList.querySelector('.collection-items');
      if (existing) renderCollectionItems(data, existing, true);
    } else {
      renderCollections(data);
    }
  } catch (e) {
    els.collectionsList.innerHTML = `<div class="empty-state" style="color:var(--danger)">${esc(e.message)}</div>`;
  }
}

export function renderCollections(data) {
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

export function selectCollection(collectionId) {
  state.activeCollection = collectionId;
  state.activeDocument = null;

  els.collectionsList.querySelectorAll('.collection-item').forEach(el => {
    el.classList.toggle('active', el.querySelector('.coll-name')?.textContent === collectionId);
  });

  const last = state.navStack[state.navStack.length - 1];
  if (!last || last.type !== 'collection' || last.id !== collectionId) {
    if (last && last.type === 'document') {
      state.navStack.pop();
    }
    const parent = currentParent();
    state.navStack.push({
      type: 'collection',
      id: collectionId,
      resourceName: `${parent}/${collectionId}`
    });
    state.navStack[state.navStack.length - 1].parentForDocs = parent;
  }

  _renderBreadcrumb();
  _closeEditor();
  loadDocuments(collectionId);
}

// ── Documents panel ────────────────────────────────────────────────────────

export function clearDocuments() {
  els.documentsPanelTitle.textContent = 'Select a collection';
  els.documentsList.innerHTML = '<div class="empty-state">Select a collection to view documents.</div>';
  els.btnNewDocument.classList.add('hidden');
  state.docPageToken = null;
}

export async function loadDocuments(collectionId, pageToken = null) {
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
    const more = fieldKeys.length > 3
      ? `<div class="doc-field" style="color:var(--text-muted)">+${fieldKeys.length - 3} more fields</div>`
      : '';

    el.innerHTML = `
      <div class="doc-id" title="${esc(doc.resourceName)}">${esc(doc.documentId)}</div>
      <div class="doc-preview">${previewFields}${more}</div>
    `;

    el.addEventListener('click', () => openDocument(doc.resourceName, collectionId));
    container.appendChild(el);
  });

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

// ── Document opening & subcollections ─────────────────────────────────────

// Injected by app.js
let _showEditorView;
let _showEditorError;
export function setEditorCallbacks(showEditorView, showEditorError) {
  _showEditorView = showEditorView;
  _showEditorError = showEditorError;
}

export async function openDocument(resourceName, collectionId) {
  state.activeDocument = resourceName;

  els.documentsList.querySelectorAll('.document-item').forEach(el => {
    el.classList.toggle('active', el.querySelector('.doc-id')?.title === resourceName);
  });

  try {
    const data = await apiFetch(`${API}/document?resourceName=${encodeURIComponent(resourceName)}`);
    _showEditorView(data);

    const collEntry = state.navStack[state.navStack.length - 1];
    if (!collEntry || collEntry.type !== 'document' || collEntry.id !== data.documentId) {
      if (state.navStack[state.navStack.length - 1]?.type === 'document') {
        state.navStack.pop();
      }
      state.navStack.push({
        type: 'document',
        id: data.documentId,
        resourceName: resourceName,
        parentForDocs: resourceName,
      });
    }

    _renderBreadcrumb();
    loadSubcollections(resourceName);
  } catch (e) {
    _showEditorError(e.message);
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

// ── New collection modal ───────────────────────────────────────────────────

export function showNewCollectionModal() {
  els.newCollectionId.value = '';
  els.newCollectionError.textContent = '';
  els.newCollectionError.classList.add('hidden');
  els.modalNewCollection.classList.remove('hidden');
  els.newCollectionId.focus();
}

export function hideNewCollectionModal() {
  els.modalNewCollection.classList.add('hidden');
}

export async function confirmNewCollection() {
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

  const parent = currentParent();
  state.navStack.push({
    type: 'collection',
    id: id,
    resourceName: `${parent}/${id}`,
    parentForDocs: parent,
  });
  state.activeCollection = id;
  _renderBreadcrumb();

  renderCollections({ collectionIds: [], nextPageToken: null });
  els.documentsPanelTitle.textContent = id;
  els.btnNewDocument.classList.remove('hidden');
  els.documentsList.innerHTML = '<div class="empty-state">No documents yet. Create the first one.</div>';

  _showCreateMode(id);
}

// ── DOM refs (collections/documents-scoped) ────────────────────────────────

const els = {
  collectionsList:      document.getElementById('collections-list'),
  documentsPanelTitle:  document.getElementById('documents-panel-title'),
  documentsList:        document.getElementById('documents-list'),
  btnNewDocument:       document.getElementById('btn-new-document'),
  modalNewCollection:   document.getElementById('modal-new-collection'),
  newCollectionId:      document.getElementById('new-collection-id'),
  newCollectionError:   document.getElementById('new-collection-error'),
};

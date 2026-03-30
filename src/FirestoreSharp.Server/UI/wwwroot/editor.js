// ── Document editor ────────────────────────────────────────────────────────

import { state, docsBase, NAV } from '/ui/state.js';
import { API, apiFetch, esc } from '/ui/api.js';
import { renderValue } from '/ui/render.js';

// Injected by app.js to avoid a circular dependency with collections.js
let _loadDocuments;
export function setLoadDocuments(fn) { _loadDocuments = fn; }

let _clearActiveDocument;
export function setClearActiveDocument(fn) { _clearActiveDocument = fn; }

export function showEditorView(doc) {
  state.editorMode = 'view';
  els.editorPanel.classList.remove('hidden');
  els.editorTitle.textContent = doc.documentId;

  els.btnEditDoc.classList.remove('hidden');
  els.btnDeleteDoc.classList.remove('hidden');
  els.btnSaveDoc.classList.add('hidden');
  els.btnCancelEdit.classList.add('hidden');

  els.editorView.classList.remove('hidden');
  els.editorEdit.classList.add('hidden');

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
  els.editorPanel.dataset.doc = JSON.stringify(doc);
}

export function enterEditMode() {
  const doc = JSON.parse(els.editorPanel.dataset.doc || '{}');
  state.editorMode = 'edit';

  els.editorView.classList.add('hidden');
  els.editorEdit.classList.remove('hidden');
  els.btnEditDoc.classList.add('hidden');
  els.btnDeleteDoc.classList.add('hidden');
  els.btnSaveDoc.classList.remove('hidden');
  els.btnCancelEdit.classList.remove('hidden');

  els.editorTextarea.value = JSON.stringify(doc.fields || {}, null, 2);
  hideEditorError();
  els.editorTextarea.focus();
}

export function showCreateMode(collectionId) {
  state.editorMode = 'create';
  state.activeDocument = null;

  _clearActiveDocument?.();

  els.editorPanel.classList.remove('hidden');
  els.editorTitle.textContent = 'New document';
  els.editorView.classList.add('hidden');
  els.editorEdit.classList.remove('hidden');

  els.btnEditDoc.classList.add('hidden');
  els.btnDeleteDoc.classList.add('hidden');
  els.btnSaveDoc.classList.remove('hidden');
  els.btnCancelEdit.classList.remove('hidden');

  els.editorPanel.dataset.createCollection = collectionId;
  els.editorPanel.dataset.doc = '';

  els.editorTextarea.value = JSON.stringify({
    fieldName: { type: 'string', value: 'example' }
  }, null, 2);
  hideEditorError();
  els.editorTextarea.focus();
}

export function closeEditor() {
  els.editorPanel.classList.add('hidden');
  state.editorMode = null;
  state.activeDocument = null;
  _clearActiveDocument?.();
}

export async function saveDocument() {
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
      const collEntry = state.navStack.find(n => n.type === NAV.COLLECTION && n.id === collectionId);
      const parent = collEntry?.parentForDocs ?? docsBase();

      const body = JSON.stringify({ documentId: null, fields });
      const data = await apiFetch(
        `${API}/document?parent=${encodeURIComponent(parent)}&collectionId=${encodeURIComponent(collectionId)}`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body }
      );
      showEditorView(data);
      state.activeDocument = data.resourceName;
      _loadDocuments(collectionId);
    } else {
      const resourceName = state.activeDocument;
      const body = JSON.stringify({ fields, updateMask: null });
      const data = await apiFetch(
        `${API}/document?resourceName=${encodeURIComponent(resourceName)}`,
        { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body }
      );
      showEditorView(data);
      const collEntry = state.navStack.find(n => n.type === NAV.COLLECTION);
      if (collEntry) _loadDocuments(collEntry.id);
    }
  } catch (e) {
    showEditorError(e.message);
  }
}

export async function deleteDocument(onDeleted) {
  const resourceName = state.activeDocument;
  if (!resourceName) return;
  if (!confirm(`Delete document "${resourceName.split('/').pop()}"? This cannot be undone.`)) return;

  try {
    await apiFetch(`${API}/document?resourceName=${encodeURIComponent(resourceName)}`, { method: 'DELETE' });

    if (state.navStack[state.navStack.length - 1]?.type === NAV.DOCUMENT &&
        state.navStack[state.navStack.length - 1]?.resourceName === resourceName) {
      state.navStack.pop();
    }

    onDeleted();
  } catch (e) {
    alert('Delete failed: ' + e.message);
  }
}

export function cancelEdit() {
  if (state.editorMode === 'create') {
    closeEditor();
  } else {
    const doc = JSON.parse(els.editorPanel.dataset.doc || '{}');
    showEditorView(doc);
  }
}

export function showEditorError(msg) {
  els.editorError.textContent = msg;
  els.editorError.classList.remove('hidden');
}

export function hideEditorError() {
  els.editorError.classList.add('hidden');
}

// ── DOM refs (editor-scoped) ───────────────────────────────────────────────

const els = {
  editorPanel:    document.getElementById('editor-panel'),
  editorTitle:    document.getElementById('editor-title'),
  editorView:     document.getElementById('editor-view'),
  editorEdit:     document.getElementById('editor-edit'),
  editorTextarea: document.getElementById('editor-textarea'),
  editorError:    document.getElementById('editor-error'),
  btnEditDoc:     document.getElementById('btn-edit-doc'),
  btnDeleteDoc:   document.getElementById('btn-delete-doc'),
  btnSaveDoc:     document.getElementById('btn-save-doc'),
  btnCancelEdit:  document.getElementById('btn-cancel-edit'),
  documentsList:  document.getElementById('documents-list'),
};

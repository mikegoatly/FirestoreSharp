// FirestoreSharp Emulator UI
// Vanilla JS, no dependencies.

import { state } from '/ui/state.js';
import { API, apiFetch, esc } from '/ui/api.js';
import {
  loadCollections, renderCollections, selectCollection,
  loadDocuments, clearDocuments, openDocument,
  showNewCollectionModal, hideNewCollectionModal, confirmNewCollection,
  setCallbacks, setEditorCallbacks,
} from '/ui/collections.js';
import {
  showEditorView, enterEditMode, showCreateMode, closeEditor,
  saveDocument, deleteDocument, cancelEdit,
  setLoadDocuments,
} from '/ui/editor.js';
import { initSelector, updateSelectorState, setResetNavigation } from '/ui/selector.js';

// ── DOM refs ───────────────────────────────────────────────────────────────

const $ = id => document.getElementById(id);

const els = {
  breadcrumb:           $('breadcrumb'),
  btnNewCollection:     $('btn-new-collection'),
  btnNewDocument:       $('btn-new-document'),
  btnEditDoc:           $('btn-edit-doc'),
  btnDeleteDoc:         $('btn-delete-doc'),
  btnSaveDoc:           $('btn-save-doc'),
  btnCancelEdit:        $('btn-cancel-edit'),
  btnCloseEditor:       $('btn-close-editor'),
  btnCancelCollection:  $('btn-cancel-collection'),
  btnConfirmCollection: $('btn-confirm-collection'),
  newCollectionId:      $('new-collection-id'),
  modalNewCollection:   $('modal-new-collection'),
  metaProject:          $('meta-project'),
  metaDatabase:         $('meta-database'),
};

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
        state.navStack = [];
        state.activeCollection = null;
        state.activeDocument = null;
        closeEditor();
        renderBreadcrumb();
        loadCollections();
        clearDocuments();
      } else {
        const item = state.navStack[idx];
        state.navStack = state.navStack.slice(0, idx + 1);
        state.activeDocument = null;
        closeEditor();
        renderBreadcrumb();

        if (item.type === 'document') {
          loadCollections();
          clearDocuments();
        } else {
          loadCollections();
          state.activeCollection = item.id;
          loadDocuments(item.id);
        }
      }
    });
  });
}

// ── Navigation reset (used by selector) ───────────────────────────────────

function resetNavigation() {
  state.navStack = [];
  state.activeCollection = null;
  state.activeDocument = null;
  closeEditor();
  renderBreadcrumb();
  loadCollections();
  clearDocuments();
}

// ── Wire up cross-module callbacks ─────────────────────────────────────────

setCallbacks(closeEditor, renderBreadcrumb, showCreateMode);
setEditorCallbacks(showEditorView, showEditorError);
setLoadDocuments(loadDocuments);
setResetNavigation(resetNavigation);

function showEditorError(msg) {
  // Thin bridge — editor.js owns its own els, so we re-export this here
  // for collections.js to call via setEditorCallbacks
  const el = document.getElementById('editor-error');
  el.textContent = msg;
  el.classList.remove('hidden');
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
els.btnDeleteDoc.addEventListener('click', () => deleteDocument(() => {
  closeEditor();
  renderBreadcrumb();
  const collEntry = state.navStack[state.navStack.length - 1];
  if (collEntry && collEntry.type === 'collection') {
    loadDocuments(collEntry.id);
    loadCollections();
  } else {
    loadCollections();
    clearDocuments();
  }
}));
els.btnSaveDoc.addEventListener('click', saveDocument);
els.btnCancelEdit.addEventListener('click', cancelEdit);
els.btnCloseEditor.addEventListener('click', () => {
  if (state.editorMode === 'view') {
    closeEditor();
  } else {
    cancelEdit();
  }
});

initSelector();

// ── Init ───────────────────────────────────────────────────────────────────

async function init() {
  try {
    const config = await apiFetch(`${API}/config`);
    state.project = config.project;
    state.database = config.database;
    state.knownDatabases = config.knownDatabases || [];
    els.metaProject.textContent = config.project;
    els.metaDatabase.textContent = config.database;
    updateSelectorState();
  } catch {
    els.metaProject.textContent = 'local';
    els.metaDatabase.textContent = '(default)';
  }

  renderBreadcrumb();
  loadCollections();
}

init();

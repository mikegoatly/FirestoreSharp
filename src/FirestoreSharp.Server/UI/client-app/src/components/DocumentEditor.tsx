import { useState } from 'react'
import { api } from '../api/client'
import type { UiValue } from '../api/types'
import { useNavStore } from '../store/navStore'
import './DocumentEditor.css'

export function DocumentEditor() {
  const { editorMode, editorDocument, editorParent, editorCollectionId, closeEditor, selectDocument } =
    useNavStore()

  const [jsonText, setJsonText] = useState(() => {
    if (editorMode === 'edit' && editorDocument) {
      return JSON.stringify(editorDocument.fields, null, 2)
    }
    return '{}'
  })
  const [error, setError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [docId, setDocId] = useState('')

  if (!editorMode) return null

  const handleSave = async () => {
    let fields: Record<string, UiValue>
    try {
      fields = JSON.parse(jsonText) as Record<string, UiValue>
    } catch {
      setError('Invalid JSON')
      return
    }

    setSaving(true)
    setError(null)
    try {
      if (editorMode === 'edit' && editorDocument) {
        const updated = await api.updateDocument(editorDocument.resourceName, { fields })
        selectDocument(updated.resourceName)
        closeEditor()
      } else if (editorMode === 'create' && editorParent && editorCollectionId) {
        const created = await api.createDocument(editorParent, editorCollectionId, {
          documentId: docId.trim() || undefined,
          fields,
        })
        selectDocument(created.resourceName)
        closeEditor()
      }
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!editorDocument) return
    if (!confirm(`Delete document "${editorDocument.documentId}"?`)) return
    setDeleting(true)
    setError(null)
    try {
      await api.deleteDocument(editorDocument.resourceName)
      closeEditor()
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setDeleting(false)
    }
  }

  const title = editorMode === 'create' ? 'New Document' : `Edit: ${editorDocument?.documentId ?? ''}`

  return (
    <div className="editor-overlay">
      <div className="editor-panel">
        <div className="editor-panel-header">
          <span className="panel-title">{title}</span>
          <div className="editor-actions">
            {editorMode === 'edit' && (
              <button className="btn-sm btn-danger" onClick={handleDelete} disabled={deleting}>
                {deleting ? 'Deleting…' : 'Delete'}
              </button>
            )}
            <button className="btn-sm btn-secondary" onClick={closeEditor} disabled={saving}>
              Cancel
            </button>
            <button className="btn-sm btn-primary" onClick={handleSave} disabled={saving}>
              {saving ? 'Saving…' : 'Save'}
            </button>
          </div>
        </div>

        <div className="editor-body">
          {editorMode === 'create' && (
            <div className="editor-id-row">
              <label>
                Document ID <span className="meta-label">(leave blank for auto)</span>
              </label>
              <input
                type="text"
                value={docId}
                onChange={(e) => setDocId(e.target.value)}
                placeholder="auto-generated"
                className="editor-id-input"
              />
            </div>
          )}

          <div className="editor-help">
            Fields JSON. Type strings:{' '}
            <code>"null" | "bool" | "int" | "double" | "timestamp" | "string" | "bytes" | "reference" | "geopoint" | "array" | "map"</code>
            <br />
            Example: <code>{'{"name": {"type": "string", "value": "Alice"}}'}</code>
          </div>

          <textarea
            className="editor-textarea"
            value={jsonText}
            onChange={(e) => setJsonText(e.target.value)}
            spellCheck={false}
          />

          {error && <div className="editor-error">{error}</div>}
        </div>
      </div>
    </div>
  )
}

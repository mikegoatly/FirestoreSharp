import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DocumentResponse } from '../api/types'
import { useNavStore } from '../store/navStore'
import { FieldValue } from './FieldValue'
import './DocumentDetail.css'

interface Props {
  resourceName: string
}

export function DocumentDetail({ resourceName }: Props) {
  const [doc, setDoc] = useState<DocumentResponse | null>(null)
  const [subcollections, setSubcollections] = useState<string[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const { openEditor, drillIntoSubcollection } = useNavStore()

  useEffect(() => {
    setLoading(true)
    setError(null)
    Promise.all([
      api.getDocument(resourceName),
      api.listCollections(resourceName),
    ])
      .then(([docRes, collRes]) => {
        setDoc(docRes)
        setSubcollections(collRes.collectionIds)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [resourceName])

  if (loading) return <div className="empty-state">Loading…</div>
  if (error) return <div className="empty-state" style={{ color: 'var(--danger)' }}>{error}</div>
  if (!doc) return null

  const fieldEntries = Object.entries(doc.fields)

  return (
    <div className="doc-detail">
      <div className="doc-meta">
        <div>ID: <span style={{ fontFamily: 'monospace', color: 'var(--accent)' }}>{doc.documentId}</span></div>
        {doc.createTime && <div>Created: {new Date(doc.createTime).toLocaleString()}</div>}
        {doc.updateTime && <div>Updated: {new Date(doc.updateTime).toLocaleString()}</div>}
      </div>

      <div className="editor-actions-bar">
        <button className="btn-sm btn-primary" onClick={() => openEditor('edit', doc)}>Edit</button>
      </div>

      {fieldEntries.length === 0 ? (
        <div className="empty-state">No fields</div>
      ) : (
        fieldEntries.map(([key, val]) => (
          <div key={key} className="editor-field">
            <div className="editor-field-key">{key}</div>
            <div className="editor-field-value">
              <FieldValue value={val} />
            </div>
          </div>
        ))
      )}

      {subcollections.length > 0 && (
        <div className="editor-subcollections">
          <div className="editor-subcollections-header">Subcollections</div>
          <div className="editor-subcollections-body">
            {subcollections.map((id) => (
              <div
                key={id}
                className="subcoll-item"
                onClick={() => drillIntoSubcollection(resourceName, id)}
              >
                <span className="coll-icon">⊞</span>
                <span className="coll-name">{id}</span>
                <span className="subcoll-arrow">›</span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

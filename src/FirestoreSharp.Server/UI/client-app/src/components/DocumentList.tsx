import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { DocumentSummary } from '../api/types'
import { useNavStore } from '../store/navStore'
import { FieldValue } from './FieldValue'

interface Props {
  parent: string
  collectionId: string
  columnIndex: number
}

export function DocumentList({ parent, collectionId, columnIndex }: Props) {
  const [documents, setDocuments] = useState<DocumentSummary[]>([])
  const [nextPageToken, setNextPageToken] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)

  const { selectDocument, openEditor, columns } = useNavStore()

  // Determine active document
  const nextCol = columns[columnIndex + 1]
  const activeResourceName =
    nextCol?.type === 'detail' ? nextCol.resourceName : null

  useEffect(() => {
    setLoading(true)
    setError(null)
    api
      .listDocuments(parent, collectionId)
      .then((res) => {
        setDocuments(res.documents)
        setNextPageToken(res.nextPageToken)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [parent, collectionId])

  const loadMore = () => {
    if (!nextPageToken) return
    setLoadingMore(true)
    api
      .listDocuments(parent, collectionId, nextPageToken)
      .then((res) => {
        setDocuments((prev) => [...prev, ...res.documents])
        setNextPageToken(res.nextPageToken)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoadingMore(false))
  }

  const handleAdd = () => {
    openEditor('create', null, parent, collectionId)
  }

  if (loading) return <div className="empty-state">Loading…</div>
  if (error) return <div className="empty-state" style={{ color: 'var(--danger)' }}>{error}</div>

  return (
    <>
      {documents.length === 0 && (
        <div className="empty-state">No documents</div>
      )}
      {documents.map((doc) => {
        const previewEntries = Object.entries(doc.fields).slice(0, 3)
        return (
          <div
            key={doc.resourceName}
            className={`document-item${activeResourceName === doc.resourceName ? ' active' : ''}`}
            onClick={() => selectDocument(doc.resourceName)}
          >
            <div className="doc-id">{doc.documentId}</div>
            <div className="doc-preview">
              {previewEntries.map(([k, v]) => (
                <div key={k} className="doc-field">
                  <span className="field-key">{k}</span>
                  <FieldValue value={v} inline />
                </div>
              ))}
            </div>
          </div>
        )
      })}
      {nextPageToken && (
        <div className="load-more">
          <button className="btn-load-more" onClick={loadMore} disabled={loadingMore}>
            {loadingMore ? 'Loading…' : 'Load more'}
          </button>
        </div>
      )}
      <div className="load-more">
        <button className="btn-load-more" onClick={handleAdd}>+ Add document</button>
      </div>
    </>
  )
}

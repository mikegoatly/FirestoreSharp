import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { useNavStore } from '../store/navStore'

interface Props {
  parent: string
  columnIndex: number
}

export function CollectionList({ parent, columnIndex }: Props) {
  const [collections, setCollections] = useState<string[]>([])
  const [nextPageToken, setNextPageToken] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)

  const { selectCollection, columns } = useNavStore()

  // Determine the currently selected collection from the next column
  const nextCol = columns[columnIndex + 1]
  const activeCollectionId =
    nextCol?.type === 'documents' && nextCol.parent === parent ? nextCol.collectionId : null

  useEffect(() => {
    setLoading(true)
    setError(null)
    api
      .listCollections(parent)
      .then((res) => {
        setCollections(res.collectionIds)
        setNextPageToken(res.nextPageToken)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false))
  }, [parent])

  const loadMore = () => {
    if (!nextPageToken) return
    setLoadingMore(true)
    api
      .listCollections(parent, nextPageToken)
      .then((res) => {
        setCollections((prev) => [...prev, ...res.collectionIds])
        setNextPageToken(res.nextPageToken)
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoadingMore(false))
  }

  if (loading) return <div className="empty-state">Loading…</div>
  if (error) return <div className="empty-state" style={{ color: 'var(--danger)' }}>{error}</div>
  if (collections.length === 0) return <div className="empty-state">No collections</div>

  return (
    <>
      {collections.map((id) => (
        <div
          key={id}
          className={`collection-item${activeCollectionId === id ? ' active' : ''}`}
          onClick={() => selectCollection(parent, id)}
        >
          <span className="coll-icon">⊞</span>
          <span className="coll-name">{id}</span>
        </div>
      ))}
      {nextPageToken && (
        <div className="load-more">
          <button className="btn-load-more" onClick={loadMore} disabled={loadingMore}>
            {loadingMore ? 'Loading…' : 'Load more'}
          </button>
        </div>
      )}
    </>
  )
}

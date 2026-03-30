import { useNavStore, type ColumnDescriptor } from '../store/navStore'
import './Breadcrumb.css'

interface Segment {
  label: string
  columnIndex: number
}

function getSegments(columns: ColumnDescriptor[]): Segment[] {
  const segments: Segment[] = []
  for (let i = 0; i < columns.length; i++) {
    const col = columns[i]
    if (col.type === 'collections') {
      // The root collections column has no breadcrumb segment of its own
      // unless it's a sub-collection context (parent is a document path)
      // We show breadcrumb for sub-collection drills only
    } else if (col.type === 'documents') {
      segments.push({ label: col.collectionId, columnIndex: i })
    } else if (col.type === 'detail') {
      const docId = col.resourceName.substring(col.resourceName.lastIndexOf('/') + 1)
      segments.push({ label: docId, columnIndex: i })
    }
  }
  return segments
}

export function Breadcrumb() {
  const { columns, navigateTo } = useNavStore()
  const segments = getSegments(columns)

  return (
    <nav className="breadcrumb">
      <span
        className="breadcrumb-item"
        onClick={() => navigateTo(0)}
      >
        /
      </span>
      {segments.map((seg, i) => (
        <span key={seg.columnIndex}>
          <span className="breadcrumb-sep">›</span>
          <span
            className={`breadcrumb-item${i === segments.length - 1 ? ' active' : ''}`}
            onClick={() => navigateTo(seg.columnIndex)}
          >
            {seg.label}
          </span>
        </span>
      ))}
    </nav>
  )
}

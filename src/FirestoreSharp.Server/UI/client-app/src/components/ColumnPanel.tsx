import type { ColumnDescriptor } from '../store/navStore'
import { CollectionList } from './CollectionList'
import { DocumentList } from './DocumentList'
import { DocumentDetail } from './DocumentDetail'
import './ColumnPanel.css'

interface Props {
  column: ColumnDescriptor
  columnIndex: number
}

function getPanelTitle(col: ColumnDescriptor): string {
  switch (col.type) {
    case 'collections':
      return 'Collections'
    case 'documents':
      return col.collectionId
    case 'detail':
      return col.resourceName.substring(col.resourceName.lastIndexOf('/') + 1)
  }
}

export function ColumnPanel({ column, columnIndex }: Props) {
  const title = getPanelTitle(column)

  return (
    <div className={`column-panel${column.type === 'detail' ? ' column-panel--detail' : ''}`}>
      <div className="panel-header">
        <span className="panel-title">{title}</span>
      </div>
      <div className="panel-body">
        {column.type === 'collections' && (
          <CollectionList parent={column.parent} columnIndex={columnIndex} />
        )}
        {column.type === 'documents' && (
          <DocumentList parent={column.parent} collectionId={column.collectionId} columnIndex={columnIndex} />
        )}
        {column.type === 'detail' && (
          <DocumentDetail resourceName={column.resourceName} />
        )}
      </div>
    </div>
  )
}

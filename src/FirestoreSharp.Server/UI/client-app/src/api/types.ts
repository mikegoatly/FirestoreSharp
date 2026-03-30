export interface UiValue {
  type:
    | 'null'
    | 'bool'
    | 'int'
    | 'double'
    | 'timestamp'
    | 'string'
    | 'bytes'
    | 'reference'
    | 'geopoint'
    | 'array'
    | 'map'
  value: unknown
}

export interface UiGeoPoint {
  latitude: number
  longitude: number
}

export interface CollectionListResponse {
  collectionIds: string[]
  nextPageToken: string | null
}

export interface DocumentSummary {
  resourceName: string
  documentId: string
  fields: Record<string, UiValue>
  createTime: string | null
  updateTime: string | null
}

export interface DocumentListResponse {
  documents: DocumentSummary[]
  nextPageToken: string | null
}

export interface DocumentResponse {
  resourceName: string
  documentId: string
  fields: Record<string, UiValue>
  createTime: string | null
  updateTime: string | null
}

export interface CreateDocumentRequest {
  documentId?: string
  fields?: Record<string, UiValue>
}

export interface UpdateDocumentRequest {
  fields?: Record<string, UiValue>
  updateMask?: string[]
}

export interface DatabaseInfo {
  project: string
  database: string
}

export interface ConfigResponse {
  project: string
  database: string
  knownDatabases: DatabaseInfo[]
}

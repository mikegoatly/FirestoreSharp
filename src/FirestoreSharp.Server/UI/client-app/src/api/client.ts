import type {
  CollectionListResponse,
  ConfigResponse,
  CreateDocumentRequest,
  DocumentListResponse,
  DocumentResponse,
  UpdateDocumentRequest,
} from './types'

async function apiFetch<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, options)
  if (!res.ok) {
    let message = res.statusText
    try {
      const body = (await res.json()) as { detail?: string; title?: string }
      message = body.detail ?? body.title ?? message
    } catch {
      // ignore parse error
    }
    throw new Error(message)
  }
  return res.json() as Promise<T>
}

export const api = {
  getConfig: () => apiFetch<ConfigResponse>('/api/ui/config'),

  listCollections: (parent: string, pageToken?: string) => {
    const params = new URLSearchParams({ parent })
    if (pageToken) params.set('pageToken', pageToken)
    return apiFetch<CollectionListResponse>(`/api/ui/collections?${params}`)
  },

  listDocuments: (parent: string, collectionId: string, pageToken?: string) => {
    const params = new URLSearchParams({ parent, collectionId })
    if (pageToken) params.set('pageToken', pageToken)
    return apiFetch<DocumentListResponse>(`/api/ui/documents?${params}`)
  },

  getDocument: (resourceName: string) => {
    const params = new URLSearchParams({ resourceName })
    return apiFetch<DocumentResponse>(`/api/ui/document?${params}`)
  },

  createDocument: (parent: string, collectionId: string, body: CreateDocumentRequest) => {
    const params = new URLSearchParams({ parent, collectionId })
    return apiFetch<DocumentResponse>(`/api/ui/document?${params}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  },

  updateDocument: (resourceName: string, body: UpdateDocumentRequest) => {
    const params = new URLSearchParams({ resourceName })
    return apiFetch<DocumentResponse>(`/api/ui/document?${params}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  },

  deleteDocument: (resourceName: string) => {
    const params = new URLSearchParams({ resourceName })
    return fetch(`/api/ui/document?${params}`, { method: 'DELETE' }).then((res) => {
      if (!res.ok) throw new Error(res.statusText)
    })
  },
}

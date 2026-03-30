import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { api } from '../api/client'

const mockFetch = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', mockFetch)
})

afterEach(() => {
  vi.restoreAllMocks()
})

function okResponse(body: unknown) {
  return Promise.resolve({
    ok: true,
    json: () => Promise.resolve(body),
  } as Response)
}

function errorResponse(status: number, body: unknown) {
  return Promise.resolve({
    ok: false,
    status,
    statusText: 'Error',
    json: () => Promise.resolve(body),
  } as Response)
}

describe('api.getConfig', () => {
  it('returns config on success', async () => {
    const data = { project: 'p', database: 'db', knownDatabases: [] }
    mockFetch.mockReturnValue(okResponse(data))
    const result = await api.getConfig()
    expect(result).toEqual(data)
    expect(mockFetch).toHaveBeenCalledWith('/api/ui/config', undefined)
  })

  it('throws on error response', async () => {
    mockFetch.mockReturnValue(errorResponse(500, { detail: 'Internal error' }))
    await expect(api.getConfig()).rejects.toThrow('Internal error')
  })
})

describe('api.listCollections', () => {
  it('calls correct URL', async () => {
    mockFetch.mockReturnValue(okResponse({ collectionIds: ['a'], nextPageToken: null }))
    await api.listCollections('projects/p/databases/db/documents')
    const url = mockFetch.mock.calls[0][0] as string
    expect(url).toContain('/api/ui/collections')
    expect(url).toContain('parent=projects')
  })

  it('includes pageToken when provided', async () => {
    mockFetch.mockReturnValue(okResponse({ collectionIds: [], nextPageToken: null }))
    await api.listCollections('parent', 'tok123')
    const url = mockFetch.mock.calls[0][0] as string
    expect(url).toContain('pageToken=tok123')
  })
})

describe('api.listDocuments', () => {
  it('calls correct URL', async () => {
    mockFetch.mockReturnValue(okResponse({ documents: [], nextPageToken: null }))
    await api.listDocuments('parent', 'users')
    const url = mockFetch.mock.calls[0][0] as string
    expect(url).toContain('/api/ui/documents')
    expect(url).toContain('collectionId=users')
  })
})

describe('api.getDocument', () => {
  it('calls correct URL with resourceName', async () => {
    const doc = { resourceName: 'r', documentId: 'id', fields: {}, createTime: null, updateTime: null }
    mockFetch.mockReturnValue(okResponse(doc))
    await api.getDocument('projects/p/databases/db/documents/c/doc1')
    const url = mockFetch.mock.calls[0][0] as string
    expect(url).toContain('/api/ui/document')
    expect(url).toContain('resourceName=')
  })
})

describe('api.createDocument', () => {
  it('POSTs with JSON body', async () => {
    const doc = { resourceName: 'r', documentId: 'id', fields: {}, createTime: null, updateTime: null }
    mockFetch.mockReturnValue(okResponse(doc))
    await api.createDocument('parent', 'col', { documentId: 'newdoc', fields: {} })
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ui/document'),
      expect.objectContaining({ method: 'POST' })
    )
  })
})

describe('api.updateDocument', () => {
  it('PUTs with JSON body', async () => {
    const doc = { resourceName: 'r', documentId: 'id', fields: {}, createTime: null, updateTime: null }
    mockFetch.mockReturnValue(okResponse(doc))
    await api.updateDocument('projects/p/databases/db/documents/c/doc1', { fields: {} })
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ui/document'),
      expect.objectContaining({ method: 'PUT' })
    )
  })
})

describe('api.deleteDocument', () => {
  it('DELETEs the document', async () => {
    mockFetch.mockReturnValue(Promise.resolve({ ok: true } as Response))
    await api.deleteDocument('projects/p/databases/db/documents/c/doc1')
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/ui/document'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })
})

import { describe, it, expect, beforeEach } from 'vitest'
import { useNavStore } from '../store/navStore'

beforeEach(() => {
  useNavStore.setState({
    project: '',
    database: '',
    knownDatabases: [],
    columns: [],
    editorMode: null,
    editorDocument: null,
    editorParent: null,
    editorCollectionId: null,
  })
})

describe('navStore', () => {
  it('loadConfig sets initial collections column', () => {
    useNavStore.getState().loadConfig('myproject', '(default)', [])
    const { columns, project, database } = useNavStore.getState()
    expect(project).toBe('myproject')
    expect(database).toBe('(default)')
    expect(columns).toHaveLength(1)
    expect(columns[0]).toMatchObject({
      type: 'collections',
      parent: 'projects/myproject/databases/(default)/documents',
    })
  })

  it('selectCollection appends documents column', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    const { columns } = useNavStore.getState()
    expect(columns).toHaveLength(2)
    expect(columns[1]).toMatchObject({ type: 'documents', parent, collectionId: 'users' })
  })

  it('selectDocument appends detail column', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    useNavStore.getState().selectDocument(`${parent}/users/doc1`)
    const { columns } = useNavStore.getState()
    expect(columns).toHaveLength(3)
    expect(columns[2]).toMatchObject({ type: 'detail', resourceName: `${parent}/users/doc1` })
  })

  it('selectCollection truncates columns beyond matching collections col', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    useNavStore.getState().selectDocument(`${parent}/users/doc1`)
    // Now select a different collection — should truncate back to 2 cols
    useNavStore.getState().selectCollection(parent, 'orders')
    const { columns } = useNavStore.getState()
    expect(columns).toHaveLength(2)
    expect(columns[1]).toMatchObject({ type: 'documents', collectionId: 'orders' })
  })

  it('navigateTo truncates columns', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    useNavStore.getState().selectDocument(`${parent}/users/doc1`)
    useNavStore.getState().navigateTo(1)
    expect(useNavStore.getState().columns).toHaveLength(2)
  })

  it('drillIntoSubcollection appends collections + documents columns', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    const docRes = `${parent}/users/doc1`
    useNavStore.getState().selectDocument(docRes)
    useNavStore.getState().drillIntoSubcollection(docRes, 'posts')
    const { columns } = useNavStore.getState()
    expect(columns).toHaveLength(5)
    expect(columns[3]).toMatchObject({ type: 'collections', parent: docRes })
    expect(columns[4]).toMatchObject({ type: 'documents', parent: docRes, collectionId: 'posts' })
  })

  it('setProjectDatabase resets navigation', () => {
    useNavStore.getState().loadConfig('p', 'db', [])
    const parent = 'projects/p/databases/db/documents'
    useNavStore.getState().selectCollection(parent, 'users')
    useNavStore.getState().setProjectDatabase('other', 'prod')
    const { columns, project, database } = useNavStore.getState()
    expect(project).toBe('other')
    expect(database).toBe('prod')
    expect(columns).toHaveLength(1)
    expect(columns[0]).toMatchObject({ type: 'collections' })
  })

  it('openEditor sets editor state', () => {
    const doc = {
      resourceName: 'projects/p/databases/db/documents/c/doc1',
      documentId: 'doc1',
      fields: {},
      createTime: null,
      updateTime: null,
    }
    useNavStore.getState().openEditor('edit', doc)
    const state = useNavStore.getState()
    expect(state.editorMode).toBe('edit')
    expect(state.editorDocument).toBe(doc)
  })

  it('closeEditor clears editor state', () => {
    useNavStore.getState().openEditor('create', null, 'parent', 'col')
    useNavStore.getState().closeEditor()
    const state = useNavStore.getState()
    expect(state.editorMode).toBeNull()
    expect(state.editorDocument).toBeNull()
    expect(state.editorParent).toBeNull()
    expect(state.editorCollectionId).toBeNull()
  })
})

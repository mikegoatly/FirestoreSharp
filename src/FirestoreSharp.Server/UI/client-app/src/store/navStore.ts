import { create } from 'zustand'
import type { DocumentResponse } from '../api/types'

export type ColumnDescriptor =
  | { type: 'collections'; parent: string }
  | { type: 'documents'; parent: string; collectionId: string }
  | { type: 'detail'; resourceName: string }

export interface NavState {
  project: string
  database: string
  knownDatabases: { project: string; database: string }[]
  columns: ColumnDescriptor[]
  editorMode: 'edit' | 'create' | null
  editorDocument: DocumentResponse | null
  editorParent: string | null
  editorCollectionId: string | null

  // Actions
  loadConfig: (project: string, database: string, knownDatabases: { project: string; database: string }[]) => void
  setProjectDatabase: (project: string, database: string) => void
  selectCollection: (parent: string, collectionId: string) => void
  selectDocument: (resourceName: string) => void
  drillIntoSubcollection: (documentResourceName: string, collectionId: string) => void
  navigateTo: (columnIndex: number) => void
  openEditor: (mode: 'edit' | 'create', doc: DocumentResponse | null, parent?: string, collectionId?: string) => void
  closeEditor: () => void
}

export const useNavStore = create<NavState>((set) => ({
  project: '',
  database: '',
  knownDatabases: [],
  columns: [],
  editorMode: null,
  editorDocument: null,
  editorParent: null,
  editorCollectionId: null,

  loadConfig: (project, database, knownDatabases) =>
    set({
      project,
      database,
      knownDatabases,
      columns: [{ type: 'collections', parent: `projects/${project}/databases/${database}/documents` }],
    }),

  setProjectDatabase: (project, database) =>
    set({
      project,
      database,
      columns: [{ type: 'collections', parent: `projects/${project}/databases/${database}/documents` }],
      editorMode: null,
      editorDocument: null,
    }),

  selectCollection: (parent, collectionId) =>
    set((state) => {
      // Find the collections column for this parent and truncate there
      const collectionsIdx = state.columns.findIndex(
        (c) => c.type === 'collections' && c.parent === parent
      )
      const base = collectionsIdx >= 0 ? state.columns.slice(0, collectionsIdx + 1) : state.columns
      return {
        columns: [...base, { type: 'documents', parent, collectionId }],
        editorMode: null,
        editorDocument: null,
      }
    }),

  selectDocument: (resourceName) =>
    set((state) => {
      // Find the documents column this document belongs to and truncate after it
      const docParent = resourceName.substring(0, resourceName.lastIndexOf('/'))
      const collectionId = docParent.substring(docParent.lastIndexOf('/') + 1)
      const parent = docParent.substring(0, docParent.lastIndexOf('/'))
      const docsIdx = state.columns.findIndex(
        (c) => c.type === 'documents' && c.parent === parent && c.collectionId === collectionId
      )
      const base = docsIdx >= 0 ? state.columns.slice(0, docsIdx + 1) : state.columns
      return {
        columns: [...base, { type: 'detail', resourceName }],
        editorMode: null,
        editorDocument: null,
      }
    }),

  drillIntoSubcollection: (documentResourceName, collectionId) =>
    set((state) => {
      // Find the detail column for this document and append after it
      const detailIdx = state.columns.findIndex(
        (c) => c.type === 'detail' && c.resourceName === documentResourceName
      )
      const base = detailIdx >= 0 ? state.columns.slice(0, detailIdx + 1) : state.columns
      const subParent = documentResourceName
      return {
        columns: [
          ...base,
          { type: 'collections', parent: subParent },
          { type: 'documents', parent: subParent, collectionId },
        ],
      }
    }),

  navigateTo: (columnIndex) =>
    set((state) => ({
      columns: state.columns.slice(0, columnIndex + 1),
      editorMode: null,
      editorDocument: null,
    })),

  openEditor: (mode, doc, parent, collectionId) =>
    set({
      editorMode: mode,
      editorDocument: doc,
      editorParent: parent ?? null,
      editorCollectionId: collectionId ?? null,
    }),

  closeEditor: () =>
    set({ editorMode: null, editorDocument: null, editorParent: null, editorCollectionId: null }),
}))

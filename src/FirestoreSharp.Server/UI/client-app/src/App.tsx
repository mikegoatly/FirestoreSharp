import { useEffect, useRef } from 'react'
import { api } from './api/client'
import { useNavStore } from './store/navStore'
import { AppHeader } from './components/AppHeader'
import { Breadcrumb } from './components/Breadcrumb'
import { ColumnPanel } from './components/ColumnPanel'
import { DocumentEditor } from './components/DocumentEditor'
import './App.css'

export function App() {
  const { columns, loadConfig, editorMode } = useNavStore()
  const workspaceRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    api
      .getConfig()
      .then((cfg) => {
        loadConfig(cfg.project, cfg.database, cfg.knownDatabases)
      })
      .catch((e: unknown) => {
        console.error('Failed to load config:', e)
      })
  }, [loadConfig])

  // Auto-scroll right when columns are added
  useEffect(() => {
    if (workspaceRef.current) {
      workspaceRef.current.scrollLeft = workspaceRef.current.scrollWidth
    }
  }, [columns.length])

  return (
    <>
      <AppHeader />
      <Breadcrumb />
      <div className="workspace" ref={workspaceRef}>
        {columns.map((col, i) => (
          <ColumnPanel key={`${col.type}-${i}`} column={col} columnIndex={i} />
        ))}
      </div>
      {editorMode && <DocumentEditor />}
    </>
  )
}

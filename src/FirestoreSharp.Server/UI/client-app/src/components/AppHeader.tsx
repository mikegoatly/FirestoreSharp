import { useNavStore } from '../store/navStore'
import './AppHeader.css'

export function AppHeader() {
  const { project, database, knownDatabases, setProjectDatabase } = useNavStore()
  const hasMultiple = knownDatabases.length > 1

  const handleChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const [proj, db] = e.target.value.split('\0')
    if (proj && db) setProjectDatabase(proj, db)
  }

  return (
    <header className="app-header">
      <div className="header-left">
        <span className="app-name">FirestoreSharp</span>
      </div>
      <div className="header-center">
        <span className="meta-label">project</span>
        {hasMultiple ? (
          <select
            className="meta-select"
            value={`${project}\0${database}`}
            onChange={handleChange}
          >
            {knownDatabases.map((db) => (
              <option key={`${db.project}\0${db.database}`} value={`${db.project}\0${db.database}`}>
                {db.project} / {db.database}
              </option>
            ))}
          </select>
        ) : (
          <>
            <span className="meta-value">{project}</span>
            <span className="meta-sep">/</span>
            <span className="meta-label">database</span>
            <span className="meta-value">{database}</span>
          </>
        )}
      </div>
      <div className="header-right" />
    </header>
  )
}

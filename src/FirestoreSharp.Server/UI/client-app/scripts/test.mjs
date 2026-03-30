/**
 * Runs vitest from the real (non-junction) project root so that Vite's
 * module loader uses consistent D:\ paths on Windows dev drives.
 *
 * realpathSync.native resolves Windows junctions (C:\dev -> D:\),
 * while realpathSync does not.
 */
import { spawnSync } from 'node:child_process'
import { realpathSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const scriptFile = fileURLToPath(import.meta.url)
// Use native realpath to follow Windows junctions (C:\dev -> D:\)
const scriptDir = realpathSync.native(dirname(scriptFile))
const projectRoot = dirname(scriptDir)
const vitestEntry = join(projectRoot, 'node_modules', 'vitest', 'vitest.mjs')
const args = process.argv.slice(2)

const result = spawnSync(process.execPath, [vitestEntry, ...args], {
  cwd: projectRoot,
  stdio: 'inherit',
})

process.exit(result.status ?? 0)

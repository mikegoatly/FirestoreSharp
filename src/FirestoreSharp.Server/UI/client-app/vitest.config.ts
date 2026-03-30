import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('.', import.meta.url))

export default defineConfig({
  plugins: [react()],
  root,
  test: {
    root,
    environment: 'jsdom',
    globals: true,
    setupFiles: ['src/__tests__/setup.ts'],
  },
})

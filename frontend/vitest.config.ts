import path from 'path'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    // Pinned so the output doesn't change with the environment: vitest picks a quieter
    // reporter when it detects an AI agent, and that one hides console warnings from
    // passing tests (React act() and key warnings among them).
    reporters: ['default'],
  },
})

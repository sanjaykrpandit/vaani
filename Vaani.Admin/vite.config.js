import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => ({
  plugins: [react()],

  server: mode === 'development'
    ? {
        port: 3000,
        open: true,
        proxy: {
          '/api': {
            target: 'https://localhost:7020',
            changeOrigin: true,
            secure: false
          }
        }
      }
    : undefined,

  build: {
    outDir: 'dist',
    sourcemap: true
  }
}))

import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// ADR 0006: the build produces static assets the backend serves, so it writes
// straight into the host project's wwwroot. In development the dev server runs
// next to `dotnet run` and forwards the API to it instead.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../Prdb.Ordeno.Host/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:8080',
    },
  },
})

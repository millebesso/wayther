import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Local development runs the Vite dev server (HMR) and proxies API calls to the
// ASP.NET app. In production the dotnet process serves the built bundle instead.
// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': 'http://localhost:5283',
    },
  },
})

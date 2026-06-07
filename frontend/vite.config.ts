import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const proxyTarget = (env.VITE_API_PROXY_TARGET || env.VITE_API_BASE_URL || 'http://localhost:5232').replace(
    /\/$/,
    '',
  )
  const port = Number(env.VITE_DEV_SERVER_PORT) || 5173

  return {
    plugins: [react()],
    build: {
      rollupOptions: {
        output: {
          manualChunks(id) {
            if (!id.includes('node_modules')) return
            if (id.includes('lightweight-charts') || id.includes('recharts')) return 'charts-vendor'
            if (id.includes('react-bootstrap') || id.includes('bootstrap')) return 'bootstrap-vendor'
            if (id.includes('react-router-dom')) return 'router-vendor'
            if (id.includes('@microsoft/signalr')) return 'signalr-vendor'
            if (id.includes('axios') || id.includes('zustand')) return 'data-vendor'
            if (id.includes('react') || id.includes('scheduler')) return 'react-vendor'
            return
          },
        },
      },
    },
    server: {
      port,
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
        },
        '/hubs': {
          target: proxyTarget,
          changeOrigin: true,
          ws: true,
        },
      },
    },
  }
})

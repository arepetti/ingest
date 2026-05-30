import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  // Resolve the API URL:
  // 1. Explicit VITE_API_URL (set by Aspire AppHost via WithEnvironment),
  // 2. Aspire service-discovery env vars (e.g. services__api__http__0),
  // 3. Local fallback for when running outside Aspire.
  const serviceDiscoveryKey = Object.keys(env).find(k => /^services__api__https?__0$/.test(k))
  const apiTarget =
    env.VITE_API_URL ||
    (serviceDiscoveryKey ? env[serviceDiscoveryKey] : undefined) ||
    'http://localhost:5000'

  const port = env.PORT ? Number(env.PORT) : 5173

  return {
    plugins: [react()],
    server: {
      host: true,
      port,
      strictPort: true,
      proxy: {
        '/api':     { target: apiTarget, changeOrigin: true, secure: false },
        '/odata':   { target: apiTarget, changeOrigin: true, secure: false },
        '/swagger': { target: apiTarget, changeOrigin: true, secure: false },
        '/health':  { target: apiTarget, changeOrigin: true, secure: false },
      },
    },
  }
})

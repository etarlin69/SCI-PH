/// <reference types="vitest" />
import { readFileSync } from 'node:fs';
import { defineConfig, loadEnv, type ServerOptions } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * Desarrollo contra SCI-PH.Web.BFF (ver README «Desarrollo con el Web BFF»):
 * - VITE_WEB_BFF_URL: origen del BFF (perfil https de launchSettings: https://localhost:7003). Vite redirige `/api` a
 *   él, así el navegador ve un único origen: la cookie HttpOnly `__Host-sciph-refresh` es first-party, el BFF valida
 *   el Origin de la SPA (Security:AllowedOrigins) y `Retry-After` es legible.
 * - VITE_DEV_HTTPS_CERT / VITE_DEV_HTTPS_KEY (opcional): certificado de desarrollo (p. ej. el de `dotnet dev-certs`)
 *   para servir la SPA en https://localhost:5173. Sin ellos se usa http://localhost:5173, que los navegadores
 *   tratan como contexto seguro y aceptan cookies Secure.
 * Nada de esto entra en el bundle: son variables solo del servidor de desarrollo.
 */
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'VITE_');
  const bffUrl = env.VITE_WEB_BFF_URL;

  const server: ServerOptions = { port: 5173, strictPort: true };
  if (bffUrl) {
    // secure:false solo afecta al salto Vite→BFF en localhost (certificado de desarrollo); no cambia la cookie.
    server.proxy = { '/api': { target: bffUrl, changeOrigin: true, secure: false } };
  }
  if (env.VITE_DEV_HTTPS_CERT && env.VITE_DEV_HTTPS_KEY) {
    server.https = { cert: readFileSync(env.VITE_DEV_HTTPS_CERT), key: readFileSync(env.VITE_DEV_HTTPS_KEY) };
  }

  return {
    plugins: [react()],
    server,
    build: {
      // El chunk diferido del Dashboard incluye Recharts (~450 kB sin comprimir).
      chunkSizeWarningLimit: 600,
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      css: false,
      restoreMocks: true,
    },
  };
});

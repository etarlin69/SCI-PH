import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
    // URL del Web BFF para el proxy de desarrollo (ver .env.example). Sin ella no hay proxy: no se asume ningún host.
    const webBffUrl = loadEnv(mode, '.', 'VITE_').VITE_WEB_BFF_URL;

    return {
        plugins: [react()],
        build: {
            // El chunk diferido del Dashboard (React.lazy en App.tsx) incluye Recharts: ~590 kB sin comprimir, ~173 kB gzip.
            // El chunk inicial (shell + módulo hospitalario) queda en ~265 kB.
            chunkSizeWarningLimit: 600,
        },
        server: webBffUrl
            ? {
                  proxy: {
                      // Mismo origen para el navegador: la cookie __Host-sciph-refresh y la cabecera CSRF funcionan sin CORS.
                      '/api': { target: webBffUrl, changeOrigin: true, secure: false },
                  },
              }
            : undefined,
    };
});

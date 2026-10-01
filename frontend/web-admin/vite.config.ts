import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    // El chunk del Dashboard incluye Recharts (~560 kB sin comprimir, ~155 kB gzip); ya se carga de forma diferida.
    chunkSizeWarningLimit: 600,
  },
});

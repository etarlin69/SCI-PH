import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
    plugins: [react()],
    build: {
        // El chunk diferido del Dashboard (React.lazy en App.tsx) incluye Recharts: ~590 kB sin comprimir, ~173 kB gzip.
        // El chunk inicial queda en ~210 kB.
        chunkSizeWarningLimit: 600,
    },
});

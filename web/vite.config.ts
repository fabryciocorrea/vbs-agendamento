import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Em desenvolvimento, a tela (porta 5173) repassa /api para o servidor (porta 5000).
// Assim o navegador fala só com um endereço e não há problema de CORS.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5000', changeOrigin: true }
    }
  }
});

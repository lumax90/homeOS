import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { uiLocalization } from '../tools/ui-localization.mjs';
import { uiControls } from '../tools/ui-controls.mjs';

export default defineConfig({
  plugins: [uiControls(), uiLocalization(), react(), tailwindcss()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': 'http://127.0.0.1:5080', '/game-runtime': 'http://127.0.0.1:5080' },
  },
  build: {
    outDir: '../server/HomeServer.Api/wwwroot',
    emptyOutDir: true,
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'react', test: /node_modules[\\/](react|react-dom|scheduler)[\\/]/ },
            { name: 'motion', test: /node_modules[\\/](motion|framer-motion|motion-dom|motion-utils)[\\/]/ },
            { name: 'ui', test: /node_modules[\\/](@heroui|react-aria|react-aria-components|@react-aria|@react-stately|@internationalized)[\\/]/ },
          ],
        },
      },
    },
  },
});

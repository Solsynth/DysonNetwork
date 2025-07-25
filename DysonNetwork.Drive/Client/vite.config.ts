import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueJsx from '@vitejs/plugin-vue-jsx'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'

// https://vite.dev/config/
export default defineConfig({
  base: '/',
  plugins: [vue(), vueJsx(), vueDevTools(), tailwindcss()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    rollupOptions: {
      output: {
        entryFileNames: `assets/[name].js`,
        chunkFileNames: `assets/[name].js`,
        assetFileNames: `assets/[name].[ext]`,
      },
    },
  },
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5216',
        changeOrigin: true,
      },
      '/cgi': {
        target: 'http://localhost:5216',
        changeOrigin: true,
      }
    },
  },
})

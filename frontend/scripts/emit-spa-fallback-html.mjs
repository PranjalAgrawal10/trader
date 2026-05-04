import { copyFileSync, existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = join(dirname(fileURLToPath(import.meta.url)), '..')
const dist = join(root, 'dist')
const indexHtml = join(dist, 'index.html')
const fallback = join(dist, '404.html')

if (!existsSync(indexHtml)) {
  console.error('emit-spa-fallback-html: dist/index.html not found (run vite build first).')
  process.exit(1)
}

copyFileSync(indexHtml, fallback)
console.log('emit-spa-fallback-html: wrote dist/404.html (SPA fallback for static hosts).')

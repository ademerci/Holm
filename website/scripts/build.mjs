// The site is kept as finished static files in site/; the build copies them to dist/ for GitHub Pages and Cloudflare.
import { cp, rm } from 'node:fs/promises';
const root = new URL('../', import.meta.url);
await rm(new URL('dist/', root), { recursive: true, force: true });
await cp(new URL('site/', root), new URL('dist/', root), { recursive: true });
console.log('Copied site/ to dist/.');

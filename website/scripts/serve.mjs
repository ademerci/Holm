import http from 'node:http';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readFile, stat } from 'node:fs/promises';
const root = path.resolve(fileURLToPath(new URL('../dist/', import.meta.url)));
const { base } = JSON.parse(await readFile(path.join(root, 'build.json'), 'utf8'));
const port = Number(process.env.PORT || 4321);
const types = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.png': 'image/png',
  '.xml': 'application/xml',
  '.txt': 'text/plain',
};
export const server = http.createServer(async (req, res) => {
  try {
    let requestPath = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (requestPath === '/' && base !== '/') {
      res.writeHead(302, { Location: base });
      return res.end();
    }
    if (!requestPath.startsWith(base)) {
      res.writeHead(404);
      return res.end('Not found');
    }
    let target = path.resolve(root, requestPath.slice(base.length));
    if (target !== root && !target.startsWith(`${root}${path.sep}`)) {
      res.writeHead(403);
      return res.end();
    }
    try {
      if ((await stat(target)).isDirectory()) {
        if (!requestPath.endsWith('/')) {
          res.writeHead(301, { Location: `${requestPath}/` });
          return res.end();
        }
        target = path.join(target, 'index.html');
      }
    } catch {
      target = path.join(root, '404.html');
      res.statusCode = 404;
    }
    const bytes = await readFile(target);
    res.setHeader('Content-Type', types[path.extname(target)] || 'application/octet-stream');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Cache-Control', 'no-store');
    if (req.method === 'HEAD') return res.end();
    res.end(bytes);
  } catch {
    res.writeHead(400);
    res.end('Bad request');
  }
});
server.listen(port, '127.0.0.1', () => console.log(`Local: http://127.0.0.1:${port}${base}`));

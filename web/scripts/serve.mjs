import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { extname, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
const root = fileURLToPath(new URL('../', import.meta.url));
const branding = fileURLToPath(new URL('../../data/branding/', import.meta.url));
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.json': 'application/json', '.png': 'image/png', '.ico': 'image/x-icon' };
createServer(async (req, res) => {
  try {
    const path = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    if (path === '/config.json') {
      res.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
      res.end(JSON.stringify({ apiBaseUrl: process.env.API_BASE_URL ?? 'http://localhost:5080/' }));
      return;
    }
    const base = path.startsWith('/assets/') ? branding : root;
    const relative = path.startsWith('/assets/') ? path.slice(8) : path === '/' ? 'index.html' : path.slice(1);
    const target = resolve(base, relative);
    if (!target.startsWith(resolve(base) + sep)) { res.writeHead(403).end(); return; }
    const data = await readFile(target);
    res.writeHead(200, { 'Content-Type': types[extname(target)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(data);
  } catch { res.writeHead(404).end('Not found'); }
}).listen(5173, '127.0.0.1', () => console.log('Grindcrest preview: http://127.0.0.1:5173'));

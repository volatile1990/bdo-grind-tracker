import { cp, mkdir, readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { endpointFromConfig } from '../model.js';
const root = new URL('../', import.meta.url);
const config = { apiBaseUrl: process.env.API_BASE_URL ?? '' };
endpointFromConfig(config);
if (!config.apiBaseUrl.startsWith('https://') && process.env.ALLOW_LOCAL_API !== '1')
  throw new Error('Production needs an HTTPS API_BASE_URL. For local integration only, set ALLOW_LOCAL_API=1.');
await mkdir(new URL('dist/assets/', root), { recursive: true });
for (const file of ['index.html', 'styles.css', 'app.js', 'model.js'])
  await cp(new URL(file, root), new URL(`dist/${file}`, root));
for (const file of ['grindcrest-header.png', 'grindcrest.ico'])
  await cp(new URL(`../data/branding/${file}`, root), new URL(`dist/assets/${file}`, root));
await writeFile(new URL('dist/config.json', root), JSON.stringify(config) + '\n');
await writeFile(new URL('dist/.nojekyll', root), '');
// Verify that the publish directory contains the entry point and all its local assets.
for (const file of ['index.html', 'styles.css', 'app.js', 'model.js', 'config.json', 'assets/grindcrest-header.png', 'assets/grindcrest.ico'])
  await readFile(new URL(`dist/${file}`, root));
console.log(`Built ${fileURLToPath(new URL('dist/', root))}`);

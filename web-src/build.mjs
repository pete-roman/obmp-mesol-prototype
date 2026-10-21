// Builds the card page into ../Client/web, which the client build copies into the mod's client\web, and the same page
// with a stand-in server into ./preview (browser only, never shipped).
//   node build.mjs          (in MESOL\web-src; npm install once first)
// Open preview/index.html in Chrome to try the page without the game.
import { build } from 'esbuild';
import { cpSync, mkdirSync, readFileSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'Client', 'web');
const preview = join(here, 'preview');

rmSync(out, { recursive: true, force: true });
rmSync(preview, { recursive: true, force: true });
mkdirSync(join(out, 'fonts'), { recursive: true });

const bundle = (entry, file, banner) => build({
  entryPoints: [join(here, 'src', entry)],
  bundle: true,
  minify: true,
  format: 'iife',
  target: ['chrome120'],
  outfile: file,
  loader: { '.json': 'json' },
  legalComments: 'none',
  banner: { js: banner },
  logLevel: 'warning',
});

await bundle('main.js', join(out, 'app.js'), '/* MESOL lore card page. */');
cpSync(join(here, 'src', 'index.html'), join(out, 'index.html'));
cpSync(join(here, 'src', 'style.css'), join(out, 'style.css'));
// the offer panel: a second, small page (LorePage.cs shows it in its own viewport)
for (const name of ['panel.html', 'panel.css', 'panel.js']) cpSync(join(here, 'src', name), join(out, name));

// Kingthings Petrock: the author's files, unchanged (his EULA asks for that), with his EULA next to them.
for (const name of ['Cinzel-Bold.ttf', 'OFL.txt', 'Kingthings Petrock.ttf', 'Kingthings Petrock light.ttf', 'kingthingsEULA.txt']) {
  cpSync(join(here, 'assets', 'fonts', name), join(out, 'fonts', name));
}
mkdirSync(join(out, 'licenses'));
writeFileSync(join(out, 'licenses', 'README.txt'), [
  'Third-party parts of the MESOL card page:',
  '- Cinzel Bold (fonts/Cinzel-Bold.ttf): SIL Open Font License 1.1, ../fonts/OFL.txt',
  '- Kingthings Petrock and Petrock Light by Kevin King (fonts/Kingthings Petrock*.ttf): free for any use, files unchanged, ../fonts/kingthingsEULA.txt',
  'Everything else on the page is part of MESOL.',
  '',
].join('\n'));

// The preview: the same files plus the stand-in server, loaded before the page's own script.
cpSync(out, preview, { recursive: true });
await bundle('preview.js', join(preview, 'preview.js'), '/* MESOL browser preview: stand-in server with the lore catalog. Not part of the mod. */');
writeFileSync(join(preview, 'index.html'), readFileSync(join(out, 'index.html'), 'utf8')
  .replace('<script src="app.js"></script>', '<script src="preview.js"></script>\n  <script src="app.js"></script>'));

// Size check: client\ goes to every player who joins.
let total = 0;
let count = 0;
const walk = (d) => readdirSync(d).forEach((f) => {
  const p = join(d, f);
  if (statSync(p).isDirectory()) walk(p);
  else { total += statSync(p).size; count++; }
});
walk(out);
console.log(`Client/web: ${count} files, ${(total / 1024).toFixed(0)} KB (app.js ${(statSync(join(out, 'app.js')).size / 1024).toFixed(1)} KB)`);
console.log(`preview: ${join(preview, 'index.html')}`);

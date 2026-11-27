// The graph view's spike (#355): serves a publish built with
// -p:VarveGraphSpike=true at /ui/ and reads the spike page's timings in
// Chromium, ten fresh page loads per size; each load is one cold run and ten
// warm ones. Prints the median and the worst, in milliseconds.
//
//   node run-spike.js <published wwwroot> [sizes, comma-separated]
const http = require('http');
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const root = process.argv[2];
const sizes = (process.argv[3] || '50,100,150,200,300').split(',').map(Number);
const types = { '.js': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml', '.dat': 'application/octet-stream' };

const server = http.createServer((request, response) => {
  let file = decodeURIComponent(new URL(request.url, 'http://x').pathname).replace(/^\/ui\//, '/');
  let full = path.join(root, file);
  if (!full.startsWith(root) || !fs.existsSync(full) || fs.statSync(full).isDirectory()) full = path.join(root, 'index.html');
  response.writeHead(200, { 'Content-Type': types[path.extname(full)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  fs.createReadStream(full).pipe(response);
});

const median = values => { const s = [...values].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
const worst = values => Math.max(...values);
const f = v => v.toFixed(1).padStart(7);

server.listen(0, '127.0.0.1', async () => {
  const port = server.address().port;
  const browser = await chromium.launch();
  console.log(`Chromium ${browser.version()}, interpreted WebAssembly, the published bundle, loopback`);
  console.log('nodes | cold total median / worst | warm total median / worst | cold layout | cold render | cold frame');

  for (const n of sizes) {
    const cold = [], warm = [];
    for (let load = 0; load < 10; load++) {
      const context = await browser.newContext();
      const page = await context.newPage();
      await page.goto(`http://127.0.0.1:${port}/ui/datasets/_spike/graphs/spike?n=${n}`);
      await page.waitForSelector('#spike-result[data-done="true"]', { timeout: 180000 });
      const lines = (await page.textContent('#spike-result')).trim().split('\n').slice(1).map(l => l.split('\t'));
      for (const [kind, nodes, layout, render, frame, total] of lines) {
        (kind === 'cold' ? cold : warm).push({ nodes: +nodes, layout: +layout, render: +render, frame: +frame - +render, total: +total });
      }
      await context.close();
    }
    const t = a => a.map(r => r.total);
    console.log(`${String(cold[0].nodes).padStart(5)} | ${f(median(t(cold)))} / ${f(worst(t(cold)))}   | ${f(median(t(warm)))} / ${f(worst(t(warm)))}   | ${f(median(cold.map(r => r.layout)))} | ${f(median(cold.map(r => r.render)))} | ${f(median(cold.map(r => r.frame)))}`);
  }

  await browser.close();
  server.close();
});

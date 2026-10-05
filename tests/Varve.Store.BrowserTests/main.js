// The page. By default it starts .NET in a dedicated worker, which is where
// synchronous access handles exist and the OPFS backend is chosen; with
// ?thread=main it starts .NET on the page's own thread, where they do not and
// the IndexedDB backend is chosen (ADR 0084). Either way the report is left on
// globalThis.varveReport for eng/browser-tests.cs.
const show = report => {
  document.getElementById('out').textContent = report;
  globalThis.varveReport = report;
};

if (new URLSearchParams(location.search).get('thread') === 'main') {
  const { run } = await import('./run.js');
  show(await run('main'));
} else {
  const worker = new Worker('./worker.js', { type: 'module' });
  worker.onmessage = e => show(e.data);
  worker.onerror = e => show('FAIL the worker failed: ' + e.message);
}

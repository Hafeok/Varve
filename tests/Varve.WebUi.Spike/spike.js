// The graph view's spike (#355): resolves after the browser's next frame,
// with the time it took, so the spike measures paint as well as render.
export function nextFrame() {
  return new Promise(resolve => {
    const start = performance.now();
    requestAnimationFrame(() => setTimeout(() => resolve(performance.now() - start), 0));
  });
}

// Starts .NET in whichever context imports this, registers the test app's own
// helpers (raw access to what the backends wrote, for the determinism check and
// for cleaning up), and runs the tests. Shared by the page and the worker.
import { dotnet } from './_framework/dotnet.js';

const testHelpers = {
  // Removes a directory of the origin private file system, if it exists.
  async clearOpfs(path) {
    if (typeof navigator.storage?.getDirectory !== 'function') return;
    const root = await navigator.storage.getDirectory();
    try {
      await root.removeEntry(path, { recursive: true });
    } catch (e) {
      if (e.name !== 'NotFoundError') throw e;
    }
  },
  // Removes every IndexedDB database whose name starts with prefix.
  async clearIndexedDb(prefix) {
    for (const { name } of await indexedDB.databases()) {
      if (name.startsWith(prefix)) {
        await new Promise((resolve, reject) => {
          const request = indexedDB.deleteDatabase(name);
          request.onsuccess = resolve;
          request.onerror = () => reject(request.error);
          request.onblocked = resolve;
        });
      }
    }
  },
  // The files directly in a directory of the origin private file system.
  async opfsFiles(path) {
    let dir = await navigator.storage.getDirectory();
    for (const part of path.split('/')) dir = await dir.getDirectoryHandle(part);
    const names = [];
    for await (const [name, handle] of dir.entries()) if (handle.kind === 'file') names.push(name);
    return names.sort().join('\n');
  },
  // A file's bytes, read through File, not through the backend.
  async opfsRead(path) {
    const parts = path.split('/');
    let dir = await navigator.storage.getDirectory();
    for (const part of parts.slice(0, -1)) dir = await dir.getDirectoryHandle(part);
    const file = await (await dir.getFileHandle(parts[parts.length - 1])).getFile();
    return new Uint8Array(await file.arrayBuffer());
  },
  byteLength: bytes => bytes.length,
  copyOut: (bytes, view) => view.set(bytes),
  userAgent: () => navigator.userAgent,
};

export async function run(thread) {
  try {
    const { setModuleImports, getAssemblyExports, getConfig } = await dotnet.create();
    setModuleImports('varve-browser-tests', testHelpers);
    const exports = await getAssemblyExports(getConfig().mainAssemblyName);
    return await exports.Varve.Store.BrowserTests.Program.Run(thread);
  } catch (e) {
    return 'FAIL ' + e + '\n' + (e.stack || '');
  }
}

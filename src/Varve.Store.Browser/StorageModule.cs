// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Varve.Store.Browser;

/// <summary>
/// The JavaScript half of the browser backends, shipped inside the assembly
/// and imported from a <c>data:</c> URL, so a host has nothing to deploy and
/// nothing to register (ADR 0084).
/// </summary>
/// <remarks>
/// Every function is a thin call onto one platform API. Nothing here keeps
/// state that the .NET side does not also know, except the scratch buffer a
/// synchronous read lands in before it is copied into the caller's span, and
/// the release functions of held Web Locks.
/// </remarks>
internal static class StorageModule
{
    public const string Name = "varve-browser-storage";

    public const string Source = """
        // Varve.Store.Browser: the origin private file system and IndexedDB (ADR 0084).

        const locked = e => e && (e.name === 'NoModificationAllowedError' || e.name === 'InvalidStateError');
        const missing = e => e && e.name === 'NotFoundError';

        // --- capability ---------------------------------------------------------

        export function opfsAvailable() {
          return typeof FileSystemSyncAccessHandle === 'function'
            && typeof FileSystemFileHandle === 'function'
            && typeof FileSystemFileHandle.prototype.createSyncAccessHandle === 'function'
            && typeof FileSystemFileHandle.prototype.move === 'function'
            && typeof navigator === 'object' && !!navigator.storage
            && typeof navigator.storage.getDirectory === 'function';
        }

        export function indexedDbAvailable() {
          return typeof indexedDB === 'object' && indexedDB !== null
            && typeof navigator === 'object' && !!navigator.locks;
        }

        // --- the origin private file system -------------------------------------

        async function walk(dir, parts, create) {
          for (const part of parts) {
            dir = await dir.getDirectoryHandle(part, { create });
          }
          return dir;
        }

        const split = path => path.split('/').filter(p => p.length > 0);

        export async function opfsDirectory(path) {
          return await walk(await navigator.storage.getDirectory(), split(path), true);
        }

        export async function opfsChild(dir, name) {
          return await dir.getDirectoryHandle(name, { create: true });
        }

        // Every file under dir, as paths relative to it with '/' between parts.
        export async function opfsFiles(dir) {
          const out = [];
          async function visit(d, prefix) {
            for await (const [name, handle] of d.entries()) {
              if (handle.kind === 'directory') {
                await visit(handle, prefix + name + '/');
              } else {
                out.push(prefix + name);
              }
            }
          }
          await visit(dir, '');
          return out.join('\n');
        }

        async function fileHandle(dir, path, create) {
          const parts = split(path);
          const parent = await walk(dir, parts.slice(0, -1), create);
          return await parent.getFileHandle(parts[parts.length - 1], { create });
        }

        // A synchronous access handle, or null when another holds the file.
        export async function opfsOpen(dir, path, create) {
          const file = await fileHandle(dir, path, create);
          try {
            return await file.createSyncAccessHandle();
          } catch (e) {
            if (locked(e)) return null;
            throw e;
          }
        }

        // 0 moved, 1 the destination is held open. Replaces the destination.
        export async function opfsMove(dir, from, to) {
          const file = await fileHandle(dir, from, false);
          const parts = split(to);
          const parent = await walk(dir, parts.slice(0, -1), true);
          try {
            await file.move(parent, parts[parts.length - 1]);
            return 0;
          } catch (e) {
            if (locked(e)) return 1;
            throw e;
          }
        }

        // 0 removed, 1 there was none, 2 it is held open.
        export async function opfsRemove(dir, path) {
          const parts = split(path);
          let parent;
          try {
            parent = await walk(dir, parts.slice(0, -1), false);
            await parent.removeEntry(parts[parts.length - 1]);
            return 0;
          } catch (e) {
            if (missing(e)) return 1;
            if (locked(e)) return 2;
            throw e;
          }
        }

        export async function opfsReadText(dir, path) {
          try {
            return await (await (await fileHandle(dir, path, false)).getFile()).text();
          } catch (e) {
            return null;
          }
        }

        let scratch = new Uint8Array(65536);

        // FileSystemSyncAccessHandle is synchronous: these return to .NET with the
        // bytes already moved, which is what ADR 0071's synchronous read needs.
        export function syncRead(handle, view, at) {
          const length = view.byteLength;
          if (scratch.length < length) scratch = new Uint8Array(Math.max(length, scratch.length * 2));
          const target = scratch.subarray(0, length);
          const read = handle.read(target, { at });
          view.set(target.subarray(0, read));
          return read;
        }

        export function syncWrite(handle, view, at) {
          const bytes = view.slice();
          let written = 0;
          while (written < bytes.length) {
            written += handle.write(bytes.subarray(written), { at: at + written });
          }
          return written;
        }

        export function syncFlush(handle) { handle.flush(); }
        export function syncSize(handle) { return handle.getSize(); }
        export function syncTruncate(handle, size) { handle.truncate(size); }
        export function syncClose(handle) { handle.close(); }

        // --- IndexedDB -------------------------------------------------------------

        const done = request => new Promise((resolve, reject) => {
          request.onsuccess = () => resolve(request.result);
          request.onerror = () => reject(request.error);
        });

        const committed = tx => new Promise((resolve, reject) => {
          tx.oncomplete = () => resolve();
          tx.onerror = () => reject(tx.error);
          tx.onabort = () => reject(tx.error || new DOMException('The transaction was aborted.', 'AbortError'));
        });

        // 'strict' asks the browser to flush to its storage before reporting the
        // transaction complete, where it distinguishes the two at all.
        const strict = (db, stores) => db.transaction(stores, 'readwrite', { durability: 'strict' });

        export async function idbOpen(name) {
          const request = indexedDB.open(name, 1);
          request.onupgradeneeded = () => {
            const db = request.result;
            db.createObjectStore('meta');
            db.createObjectStore('chunks');
            db.createObjectStore('blobs');
          };
          return await done(request);
        }

        export function idbClose(db) { db.close(); }

        // Every segment as [id, length, sealed] triples, flattened.
        export async function idbSegments(db) {
          const tx = db.transaction(['meta'], 'readonly');
          const store = tx.objectStore('meta');
          const keys = await done(store.getAllKeys(IDBKeyRange.bound('segment:', 'segment;', false, true)));
          const values = await done(store.getAll(IDBKeyRange.bound('segment:', 'segment;', false, true)));
          const out = [];
          for (let i = 0; i < keys.length; i++) {
            out.push(Number(keys[i].substring(8)) + ',' + values[i].length + ',' + (values[i].sealed ? 1 : 0));
          }
          return out.join('\n');
        }

        // Bytes already flushed, from offset, up to length: the chunk that holds
        // offset and every chunk after it below offset + length, joined.
        export async function idbReadRange(db, segment, offset, length) {
          const tx = db.transaction(['chunks'], 'readonly');
          const store = tx.objectStore('chunks');
          const before = await done(store.openCursor(IDBKeyRange.bound([segment, 0], [segment, offset]), 'prev'));
          const first = before ? before.key[1] : offset;
          const end = offset + length;
          const keys = await done(store.getAllKeys(IDBKeyRange.bound([segment, first], [segment, end], false, true)));
          const values = await done(store.getAll(IDBKeyRange.bound([segment, first], [segment, end], false, true)));
          const out = new Uint8Array(length);
          let filled = 0;
          for (let i = 0; i < keys.length; i++) {
            const at = keys[i][1];
            const chunk = new Uint8Array(values[i]);
            const from = Math.max(offset, at);
            const to = Math.min(end, at + chunk.length);
            if (to > from) {
              out.set(chunk.subarray(from - at, to - at), from - offset);
              filled = Math.max(filled, to - offset);
            }
          }
          return out.subarray(0, filled);
        }

        // One chunk and the segment's new length and seal, in one transaction.
        export async function idbAppend(db, segment, offset, bytes, length, sealed) {
          const tx = strict(db, ['meta', 'chunks']);
          if (bytes.length > 0) tx.objectStore('chunks').put(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length), [segment, offset]);
          tx.objectStore('meta').put({ length, sealed }, 'segment:' + String(segment).padStart(8, '0'));
          await committed(tx);
        }

        export async function idbGetMeta(db, key) {
          const tx = db.transaction(['meta'], 'readonly');
          const value = await done(tx.objectStore('meta').get(key));
          return value === undefined ? null : new Uint8Array(value);
        }

        // false when the key already has a value: a value written once.
        export async function idbPutMetaOnce(db, key, bytes) {
          const tx = strict(db, ['meta']);
          const store = tx.objectStore('meta');
          const existing = await done(store.getKey(key));
          if (existing !== undefined) {
            tx.abort();
            return false;
          }
          store.put(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length), key);
          await committed(tx);
          return true;
        }

        export async function idbGetBlob(db, name) {
          const tx = db.transaction(['blobs'], 'readonly');
          const value = await done(tx.objectStore('blobs').get(name));
          return value === undefined ? null : new Uint8Array(value);
        }

        export async function idbPutBlob(db, name, bytes) {
          const tx = strict(db, ['blobs']);
          tx.objectStore('blobs').put(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.length), name);
          await committed(tx);
        }

        export async function idbDeleteBlob(db, name) {
          const tx = strict(db, ['blobs']);
          const store = tx.objectStore('blobs');
          const existing = await done(store.getKey(name));
          if (existing === undefined) {
            tx.abort();
            return false;
          }
          store.delete(name);
          await committed(tx);
          return true;
        }

        export async function idbBlobNames(db) {
          const tx = db.transaction(['blobs'], 'readonly');
          return (await done(tx.objectStore('blobs').getAllKeys())).map(String).join('\n');
        }

        // --- the lease: a Web Lock held until released ------------------------------

        export async function lockAcquire(name) {
          return await new Promise((resolve, reject) => {
            navigator.locks.request(name, { ifAvailable: true }, lock => {
              if (!lock) {
                resolve(null);
                return undefined;
              }
              return new Promise(release => resolve({ release }));
            }).catch(reject);
          });
        }

        export function lockRelease(held) { held.release(); }

        // --- bytes across the boundary -------------------------------------------

        // A copy of a span the call lends, for an asynchronous call to keep.
        export function stage(view) { return view.slice(); }
        export function byteLength(bytes) { return bytes.length; }
        export function copyOut(bytes, offset, view) {
          view.set(bytes.subarray(offset, offset + view.byteLength));
        }
        """;
}

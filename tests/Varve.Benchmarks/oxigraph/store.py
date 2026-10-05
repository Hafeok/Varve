# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
"""Times milestone 6a's file-backed store workloads in pyoxigraph, on disk.

    dotnet run -c Release --project tests/Varve.Benchmarks -- --update-export update/
    python store.py update/store.nq

pyoxigraph 0.5.11 through its Python binding, with a RocksDB store in a
temporary directory (ox.Store(path)), timed with perf_counter in one process.
The rows Varve's FileCommitBenchmarks and FileScanBenchmarks time:

- 100,000 quads as one transaction, as 100 transactions of 1,000, and 1,000
  single-quad transactions into a store of 100,000. store.extend(quads) is one
  transaction. RocksDB's write-ahead log is not synced per write by default, so
  an acknowledged transaction is not durable the way Varve's flushed commit is:
  the rows time different guarantees, and the README says so.
- Scans over 1,000,000 quads loaded with store.bulk_load: every quad, one
  predicate, the default graph, and 10,000 subject lookups, each counted by
  iterating quads_for_pattern in Python, so the per-quad cost of the binding is
  inside the time.

Median of 5 runs after 1 warm-up run, each commit row into its own new store.
"""
import os
import shutil
import statistics
import sys
import tempfile
import time

import pyoxigraph as ox


def median(run, warm=1, timed=5):
    times = []
    for index in range(warm + timed):
        elapsed = run()
        if index >= warm:
            times.append(elapsed)
    return statistics.median(times), min(times)


def quads_of(path, limit=None):
    quads = []
    for quad in ox.parse(path=path, format=ox.RdfFormat.N_QUADS):
        quads.append(quad)
        if limit is not None and len(quads) == limit:
            break
    return quads


def fresh_store():
    directory = tempfile.mkdtemp(prefix="oxigraph-6a-")
    return directory, ox.Store(directory)


def commits(path):
    first = quads_of(path, 100_000)
    singles = quads_of(path)[-1_000:]

    def one_transaction():
        directory, store = fresh_store()
        begin = time.perf_counter()
        store.extend(first)
        store.flush()
        elapsed = time.perf_counter() - begin
        del store
        shutil.rmtree(directory)
        return elapsed

    def hundred_transactions():
        directory, store = fresh_store()
        begin = time.perf_counter()
        for start in range(0, 100_000, 1_000):
            store.extend(first[start:start + 1_000])
        store.flush()
        elapsed = time.perf_counter() - begin
        del store
        shutil.rmtree(directory)
        return elapsed

    def thousand_singles():
        directory, store = fresh_store()
        store.extend(first)
        store.flush()
        begin = time.perf_counter()
        for quad in singles:
            store.add(quad)
        store.flush()
        elapsed = time.perf_counter() - begin
        del store
        shutil.rmtree(directory)
        return elapsed

    for name, run in (("100,000 quads, one transaction", one_transaction),
                      ("100,000 quads, 100 transactions of 1,000", hundred_transactions),
                      ("1,000 single-quad transactions into 100,000", thousand_singles)):
        (med, best) = median(run)
        print(f"{name}: median {med * 1000:.1f} ms, best {best * 1000:.1f} ms")


def scans(path):
    directory, store = fresh_store()
    begin = time.perf_counter()
    store.bulk_load(path=path, format=ox.RdfFormat.N_QUADS)
    store.flush()
    print(f"bulk load of 1,000,000 quads: {(time.perf_counter() - begin) * 1000:.0f} ms, {len(store)} quads")
    all_quads = quads_of(path)
    predicate = all_quads[3].predicate
    subjects = [all_quads[i * 97].subject for i in range(10_000)]

    def count(subject=None, predicate_=None, graph=None):
        n = 0
        for _ in store.quads_for_pattern(subject, predicate_, None, graph):
            n += 1
        return n

    def full():
        begin = time.perf_counter()
        assert count() == 1_000_000
        return time.perf_counter() - begin

    def bound():
        begin = time.perf_counter()
        count(predicate_=predicate)
        return time.perf_counter() - begin

    def default_graph():
        begin = time.perf_counter()
        count(graph=ox.DefaultGraph())
        return time.perf_counter() - begin

    def lookups():
        begin = time.perf_counter()
        for subject in subjects:
            count(subject=subject)
        return time.perf_counter() - begin

    for name, run in (("every quad", full), ("one predicate", bound),
                      ("the default graph", default_graph), ("10,000 subject lookups", lookups)):
        (med, best) = median(run)
        print(f"scan, {name}: median {med * 1000:.2f} ms, best {best * 1000:.2f} ms")

    sizes = 0
    for root, _, files in os.walk(directory):
        sizes += sum(os.path.getsize(os.path.join(root, f)) for f in files)
    print(f"bytes on disk after bulk load: {sizes}, per quad {sizes / 1_000_000:.1f}")
    del store
    shutil.rmtree(directory)


if __name__ == "__main__":
    print(f"pyoxigraph {ox.__version__}")
    commits(sys.argv[1])
    scans(sys.argv[1])

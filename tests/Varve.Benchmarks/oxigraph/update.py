# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
"""Times milestone 5c's workloads in pyoxigraph, for the benchmark README.

    dotnet run -c Release --project tests/Varve.Benchmarks -- --update-export update/
    python update.py update/

This is Oxigraph through its Python binding, not the Rust library, timed with
perf_counter inside one Python process:

- INSERT DATA of N quads into a new in-memory store: store.update(text), so
  parsing is inside the time, as it is for the .NET engines. Median of 10 runs
  after 2 warm-up runs, each into its own new store.
- DELETE/INSERT WHERE over the million-quad store loaded beforehand: the move
  and its reverse alternated, median of 16 requests after 4 warm-up requests,
  as the .NET run does.
- RDFC-1.0 over the three 100,000-triple graphs:
  Dataset.canonicalize(RDFC_1_0) on a fresh copy each run (the copy is not timed), and then serialising the result
  as N-Quads, which is inside the time because the .NET engines produce the
  canonical document. Median of 10 runs after 2 warm-up runs.
"""
import os
import statistics
import sys
import time

import pyoxigraph as ox


def median(run, warm, timed):
    times = []
    for index in range(warm + timed):
        elapsed = run()
        if index >= warm:
            times.append(elapsed)
    return statistics.median(times), min(times)


def insert(path):
    text = open(path, encoding="utf-8").read()
    quads = []

    def run():
        store = ox.Store()
        begin = time.perf_counter()
        store.update(text)
        elapsed = time.perf_counter() - begin
        quads.append(len(store))
        return elapsed

    return median(run, 2, 10), quads[-1]


def move(directory):
    store = ox.Store()
    begin = time.perf_counter()
    store.load(path=os.path.join(directory, "store.nq"), format=ox.RdfFormat.N_QUADS)
    loaded = time.perf_counter() - begin
    requests = [open(os.path.join(directory, name), encoding="utf-8").read() for name in ("move.ru", "back.ru")]
    turn = [0]

    def run():
        text = requests[turn[0] & 1]
        turn[0] += 1
        begin = time.perf_counter()
        store.update(text)
        return time.perf_counter() - begin

    result = median(run, 4, 16)
    moved = sum(1 for _ in store.quads_for_pattern(None, ox.NamedNode("http://example.org/store/predicate/3"), None, None)
                if not isinstance(_.graph_name, ox.DefaultGraph))
    return result, len(store), loaded, moved


def canonicalise(directory, name):
    source = ox.Dataset(ox.parse(path=os.path.join(directory, name), format=ox.RdfFormat.N_TRIPLES))
    output = [b""]

    def run():
        copy = ox.Dataset(source)
        begin = time.perf_counter()
        copy.canonicalize(ox.CanonicalizationAlgorithm.RDFC_1_0)
        output[0] = ox.serialize(copy, format=ox.RdfFormat.N_QUADS)
        return time.perf_counter() - begin

    result = median(run, 2, 10)
    # Oxigraph writes in its own order; the canonical document is the lines sorted.
    varve = open(os.path.join(directory, name.replace(".nt", ".varve.nq")), "rb").read()
    same = sorted(output[0].splitlines()) == varve.splitlines()
    return result, len(source), len(output[0]), same


def main():
    directory = sys.argv[1]
    print(f"pyoxigraph {ox.__version__}")
    for name in sorted(n for n in os.listdir(directory) if n.startswith("insert-")):
        (middle, least), quads = insert(os.path.join(directory, name))
        print(f"{name:22} median {middle * 1e3:9.1f} ms  min {least * 1e3:9.1f}  quads {quads:,}")
    (middle, least), quads, loaded, moved = move(directory)
    print(f"{'delete-insert-where':22} median {middle * 1e3:9.1f} ms  min {least * 1e3:9.1f}  store {quads:,} quads, loaded in {loaded:.1f} s, {moved:,} quads of predicate 3 in named graphs after")
    for name in ("canon-blank.nt", "canon-twins.nt", "canon-blank1000.nt"):
        (middle, least), triples, size, same = canonicalise(directory, name)
        print(f"{name:22} median {middle * 1e3:9.1f} ms  min {least * 1e3:9.1f}  {triples:,} triples, {size:,} bytes of N-Quads, same as Varve's: {same}")


if __name__ == "__main__":
    main()

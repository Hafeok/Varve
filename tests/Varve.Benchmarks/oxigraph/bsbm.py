# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
"""Times the BSBM-style query mix in pyoxigraph, for the benchmark README.

    dotnet run -c Release --project tests/Varve.Benchmarks -- --bsbm-export bsbm/
    python bsbm.py bsbm/

This is Oxigraph through its Python binding, not the Rust library: the time
is taken inside the call — perf_counter around the query and the iteration of
its results, in one Python process with the store already loaded — so it
excludes interpreter start-up and loading, but includes the binding's cost of
handing each solution to Python. Median of 30 runs after 5 warm-up runs, and
the row count, which must agree with the .NET engines'.
"""
import json
import os
import statistics
import sys
import time

import pyoxigraph as ox


def main():
    directory = sys.argv[1]
    store = ox.Store()
    started = time.perf_counter()
    store.load(path=os.path.join(directory, "bsbm.nt"), format=ox.RdfFormat.N_TRIPLES)
    print(f"pyoxigraph {ox.__version__}: loaded {len(store):,} triples in {time.perf_counter() - started:.2f} s")
    queries = json.load(open(os.path.join(directory, "queries.json"), encoding="utf-8"))
    for name, text in queries:
        times = []
        rows = 0
        for run in range(35):
            begin = time.perf_counter()
            rows = sum(1 for _ in store.query(text))
            elapsed = time.perf_counter() - begin
            if run >= 5:
                times.append(elapsed)
        print(f"{name:26} median {statistics.median(times) * 1e3:9.3f} ms  min {min(times) * 1e3:9.3f}  rows {rows}")


if __name__ == "__main__":
    main()

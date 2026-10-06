# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
"""Times pyoxigraph's bulk loader on an N-Quads file, for the milestone 6c bulk gate.

    dotnet run -c Release --project tests/Varve.Benchmarks -- --bulk-gate 10000000 --write-file q.nq
    python bulk.py q.nq

The store is on disk, in a temporary directory deleted afterwards; the time is
Store.bulk_load alone, and the size is the directory after it.
"""
import os
import shutil
import sys
import tempfile
import time

import pyoxigraph as ox


def main():
    path = sys.argv[1]
    directory = tempfile.mkdtemp(prefix="ox-bulk-")
    store = ox.Store(directory)
    started = time.perf_counter()
    store.bulk_load(path=path, format=ox.RdfFormat.N_QUADS)
    elapsed = time.perf_counter() - started
    count = len(store)
    size = sum(os.path.getsize(os.path.join(root, name)) for root, _, names in os.walk(directory) for name in names)
    print(f"pyoxigraph {ox.__version__}: {count:,} quads in {elapsed:.1f} s, {count / elapsed:,.0f} quads/s, "
          f"{size / 1e9:.2f} GB on disk ({size / count:.1f} B/quad)")
    del store
    shutil.rmtree(directory)


if __name__ == "__main__":
    main()

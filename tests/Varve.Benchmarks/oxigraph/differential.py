# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
"""Answers every exported evaluation case with pyoxigraph, for Differential.cs.

    dotnet run -c Release --project tests/Varve.Benchmarks -- --export-cases cases.json
    python differential.py cases.json answers/
    dotnet run -c Release --project tests/Varve.Benchmarks -- --differential answers/

Each case gets a fresh store; each file is one load, so blank nodes stay apart
across files as the harness keeps them. SELECT and ASK answers are written as
SPARQL JSON (N.srj), graph answers as N-Triples (N.nt), and a query Oxigraph
refuses as N.error with its message.
"""
import json
import os
import sys

import pyoxigraph as ox

FORMATS = {
    ".ttl": ox.RdfFormat.TURTLE,
    ".nt": ox.RdfFormat.N_TRIPLES,
    ".nq": ox.RdfFormat.N_QUADS,
    ".trig": ox.RdfFormat.TRIG,
    ".rdf": ox.RdfFormat.RDF_XML,
}


def answer(case, stem):
    store = ox.Store()
    for graph, path in case["graphs"]:
        fmt = FORMATS[os.path.splitext(path)[1]]
        name = ox.NamedNode(graph) if graph else ox.DefaultGraph()
        base = "https://w3c.github.io/rdf-tests/" + path.split("/tests/w3c/rdf-tests/", 1)[1]
        if fmt in (ox.RdfFormat.N_QUADS, ox.RdfFormat.TRIG):
            store.load(path=path, format=fmt, base_iri=base)
        else:
            store.load(path=path, format=fmt, base_iri=base, to_graph=name)
    with open(case["query"], encoding="utf-8") as f:
        text = f.read()
    result = store.query(text, base_iri=case["base"])
    if isinstance(result, ox.QueryTriples):
        with open(stem + ".nt", "wb") as out:
            out.write(result.serialize(format=ox.RdfFormat.N_TRIPLES))
    else:
        with open(stem + ".srj", "wb") as out:
            out.write(result.serialize(format=ox.QueryResultsFormat.JSON))


def main():
    cases = json.load(open(sys.argv[1], encoding="utf-8"))
    out = sys.argv[2]
    os.makedirs(out, exist_ok=True)
    refused = 0
    for index, case in enumerate(cases):
        stem = os.path.join(out, str(index))
        for ext in (".srj", ".nt", ".error"):
            if os.path.exists(stem + ext):
                os.remove(stem + ext)
        try:
            answer(case, stem)
        except Exception as error:  # a refusal is a result, recorded and reported
            refused += 1
            with open(stem + ".error", "w", encoding="utf-8") as f:
                f.write(type(error).__name__ + ": " + str(error))
    print(f"pyoxigraph {ox.__version__}: {len(cases)} cases, {refused} refused")


if __name__ == "__main__":
    main()

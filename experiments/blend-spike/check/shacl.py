# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# SHACL over the spike's export (#128), with pyshacl: Varve has no SHACL
# engine yet (#13), and the spike ships nothing, so an off-the-shelf one is
# used, as the plan said.
#
#   python -I check/shacl.py <spike directory>
#
# Reads out/head.nq (the dataset at its head, as the host exports it: one
# quad per line, tab-separated terms), validates the union of its graphs
# against shapes/layer.ttl, internals.ttl, level.ttl and contradiction.ttl, and each named
# graph except the computed one against emergent-computed.ttl. Writes
# out/shacl.txt.

import sys
from pathlib import Path

import pyshacl
import rdflib

EMERGENT = rdflib.URIRef("urn:varve-spike:g/emergent")
SH = rdflib.Namespace("http://www.w3.org/ns/shacl#")


def load(spike: Path) -> rdflib.Dataset:
    lines = (spike / "out" / "head.nq").read_text(encoding="utf-8").splitlines()[1:]
    text = "\n".join(" ".join(line.split("\t")) + " ." for line in lines if line)
    dataset = rdflib.Dataset()
    dataset.parse(data=text, format="nquads")
    return dataset


def results(report: rdflib.Graph) -> list[str]:
    rows = []
    for result in report.subjects(rdflib.RDF.type, SH.ValidationResult):
        rows.append(
            f"{report.value(result, SH.focusNode)}\t{report.value(result, SH.value)}\t{report.value(result, SH.resultMessage)}"
        )
    return sorted(rows)


def main() -> int:
    spike = Path(sys.argv[1])
    dataset = load(spike)
    union = rdflib.Graph()
    for graph in dataset.graphs():
        for triple in graph:
            union.add(triple)

    out = []
    for name in ("layer.ttl", "internals.ttl", "level.ttl", "contradiction.ttl"):
        shapes = rdflib.Graph().parse(spike / "shapes" / name, format="turtle")
        conforms, report, _ = pyshacl.validate(union, shacl_graph=shapes, advanced=True)
        rows = results(report)
        out.append(f"## {name}: conforms={conforms}, {len(rows)} results")
        out.extend(rows)

    shapes = rdflib.Graph().parse(spike / "shapes" / "emergent-computed.ttl", format="turtle")
    violations = 0
    for graph in dataset.graphs():
        if graph.identifier == EMERGENT or len(graph) == 0:
            continue
        conforms, report, _ = pyshacl.validate(graph, shacl_graph=shapes)
        if not conforms:
            violations += 1
            out.append(f"emergent outside the computed graph, in {graph.identifier}")
            out.extend(results(report))
    out.append(f"## emergent-computed.ttl: {violations} graphs with a hand-written blend:emergent")

    (spike / "out" / "shacl.txt").write_text("\n".join(out) + "\n", encoding="utf-8")
    print("\n".join(line for line in out if line.startswith("##")))
    return 0


if __name__ == "__main__":
    sys.exit(main())

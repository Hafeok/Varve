# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.
#
# R1's verdict against DD0001's, for every project reference in the
# repository (#128).
#
#   python -I check/compare_layers.py <spike directory> <solution project list>
#
# R1's verdicts are out/layer-verdicts.tsv; the references and their layers
# out/references.tsv; paths out/code-facts.nq. DD0001's verdict is what the
# analyzer actually reported when the project was built:
#   - every project in Varve.slnx: the solution builds with no diagnostic at
#     all, so DD0001 reported none of its references;
#   - the projects outside the solution: out/other-builds.tsv,
#     out/fixtures-dd0001.txt and out/negative-control-dd0001.txt, each a
#     build of that project with the DD0001 lines it printed.
# Writes out/layer-comparison.tsv and prints the summary.

import re
import sys
from pathlib import Path

PKG = "urn:varve-spike:pkg:"
LINE = re.compile(r"DD0001: '([^']+)' is at layer (\d+) and references '([^']+)', which is at layer (\d+)")


def name(term: str) -> str:
    term = term.strip("<>")
    return term[len(PKG):].split("@")[0] if term.startswith(PKG) else term


def rows(path: Path) -> list[list[str]]:
    lines = path.read_text(encoding="utf-8").splitlines()
    return [line.split("\t") for line in lines[1:] if line]


def main() -> int:
    spike = Path(sys.argv[1])
    out = spike / "out"
    solution = {line.strip() for line in Path(sys.argv[2]).read_text().splitlines() if line.strip()}

    paths = {}
    for line in (out / "code-facts.nq").read_text(encoding="utf-8").splitlines():
        if "<urn:varve-spike:code#path>" in line:
            subject, _, rest = line.partition(" <urn:varve-spike:code#path> ")
            paths[subject.strip("<>")] = rest.split('"')[1]

    verdicts = {(f, t): v for f, t, v in (r[:3] for r in rows(out / "layer-verdicts.tsv"))}

    built: dict[str, str] = {p: "conforms" for p in solution}
    reported: set[tuple[str, str]] = set()
    for file in ("fixtures-dd0001.txt", "negative-control-dd0001.txt", "other-builds.tsv"):
        text = (out / file).read_text(encoding="utf-8") if (out / file).exists() else ""
        for m in LINE.finditer(text):
            reported.add((m.group(1), m.group(3)))
    for path in ("tests/fixtures/layer-rule/violating/Varve.Fixture.Lower/Varve.Fixture.Lower.csproj",
                 "tests/fixtures/layer-rule/conforming/Varve.Fixture.Consumer/Varve.Fixture.Consumer.csproj",
                 "experiments/blend-spike/negative-control/Varve.Spike.Upward/Varve.Spike.Upward.csproj",
                 "experiments/blend-spike/negative-control/Varve.Spike.SameLayer/Varve.Spike.SameLayer.csproj"):
        built[path] = "conforms"
    if (out / "other-builds.tsv").exists():
        for line in (out / "other-builds.tsv").read_text(encoding="utf-8").splitlines():
            parts = line.split("\t")
            if len(parts) >= 2:
                built[parts[0]] = "conforms" if parts[1] == "succeeded" or "DD0001" in line else "unknown (" + parts[1] + ")"

    table = []
    direct = set()
    for r in rows(out / "references.tsv"):
        f, t, l, m = (r + ["", "", "", ""])[:4]
        fn, tn = name(f), name(t)
        direct.add((fn, tn))
        path = paths.get(f.strip("<>"), "?")
        r1 = {"<urn:varve-spike:lib#mayReference>": "conforms",
              "<urn:varve-spike:lib#mustNotReference>": "reported"}.get(verdicts.get((f, t), ""), "no verdict")
        if fn.endswith(".Tests"):
            dd = "not checked (test assembly)"
        elif not l:
            dd = "not checked (no ArchLayer)"
        elif not tn.startswith("Varve."):
            dd = "not checked (not in the family)"
        elif (fn, tn) in reported:
            dd = "reported"
        else:
            dd = built.get(path, "not built")
        agree = (r1 == dd) or (r1 == "no verdict" and dd.startswith("not checked"))
        if r1 == "no verdict" and not l and not dd.startswith("not checked"):
            agree = False
        outcome = "not observed" if dd.startswith(("unknown", "not built")) else "agree" if agree else "DISAGREE"
        table.append([fn, tn, l.split('"')[1] if '"' in l else "", m.split('"')[1] if '"' in m else "", r1, dd, outcome, path])

    for fn, tn in sorted(reported - direct):
        table.append([fn, tn, "", "", "no verdict (not a direct reference)", "reported (transitive)", "DISAGREE", "?"])

    header = ["from", "to", "L", "M", "R1", "DD0001", "", "project"]
    lines = ["\t".join(header)] + ["\t".join(row) for row in table]
    (out / "layer-comparison.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")

    counts: dict[str, int] = {}
    for row in table:
        key = f"{row[4]} / {row[5]} / {row[6]}"
        counts[key] = counts.get(key, 0) + 1
    print(f"{len(table)} rows")
    for key in sorted(counts):
        print(f"{counts[key]:5}  {key}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

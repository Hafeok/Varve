<!--
Pull requests are optional here. `main` is the trunk and you may commit to it
directly (ADR 0032) — a pull request is the right tool when a change wants
discussion, comes from outside, or you want it read before it lands.

The body is the report: what was decided, what was measured, what was found,
what is left. Not a changelog — the commits are the changelog. A reader should
be able to decide from the body alone whether the work is sound, without
reconstructing a finding from the diff.
-->

## What this changes, and why

## What was measured

<!--
Numbers, not adjectives. Conformance counts, allocation figures, benchmark
results with the machine stated. If nothing was measured, say so.
-->

## What was found

<!--
Anything surprising: a defect the change uncovered, a specification that
contradicts itself, an assumption that turned out to be wrong. This section is
often the most valuable one, and it is the one most often left empty.
-->

## What is left

---

## Checklist

- [ ] **Issue referenced** — every commit body carries `Refs #N` or `Closes #N`
      (ADR 0033, enforced by `eng/issue-refs.cs`)
- [ ] **ADR cited, or no behaviour was decided** — if this contradicts an
      accepted ADR, it comes with a *superseding* one; accepted ADRs are never
      edited
- [ ] **Specification updated, or n/a** — `docs/spec/` still describes what the
      code does
- [ ] **API baseline updated, or n/a** — `PublicAPI.Unshipped.txt` for new
      surface; a line removed from `PublicAPI.Shipped.txt` is a **breaking
      change** (ADR 0035)
- [ ] **Conformance baseline updated, or n/a** — cases that now pass are in
      `baseline/passing.txt`; every exemption carries a written justification
- [ ] **Tests** — real components through public contracts, doubles only at a
      true process boundary; a new streaming reader is in
      `ConformanceSuite.OracleCorpus`
- [ ] **A new gate's failure path was proven by running it**, and the output is
      quoted above
- [ ] **Signed and signed off** — every commit is signed (or is from a cloud AI
      session, which ADR 0034 exempts) and carries `Signed-off-by:`
- [ ] **A traceability record exists**, if any part of this was AI-assisted
      (`docs/traceability/`)
- [ ] **`dotnet run eng/ci.cs` passes locally**

<!--
Only the maintainer merges. Reviews are non-blocking: a green pull request is
not waiting for anybody.
-->

# RDFC-1.0's second gap: blank nodes related across graphs

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The request is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#36](https://github.com/Hafeok/Varve/issues/36), the upstream report on RDFC-1.0; referenced, not closed |
| **Date** | 2026-10-22 |
| **Tool** | Claude Code, the cloud session of #138 ([its record](2026-10-20-issue-138-shacl-part-2a.md)) |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01RFtkE4cRmAMRARHDu5Y9ZA` |
| **Branch** | `ccr-bf4bdc29-canon-graph-gap`, from `main` at 0b02803 |

## The request

> Yes, do the canonicaliser fix as a separate PR. No release descriptor. Commits reference #36, with no closing keyword, since #36 is the upstream report and stays open until the W3C answers.
> 1. **Pin the failing seed's counterexample** as a fixed test case, minimised to the smallest dataset that shows it.
> 2. **Say whether it's #36's case** (blank-node graph names). If it is a different case, say so and describe it, since that would be a second upstream gap or a bug of ours.
> 3. **Correct `docs/spec/rdf-canon.md` §6** to claim only what RDFC-1.0 guarantees, citing the RDFC-1.0 section and #36.
> 4. **Narrow the property test** to that claim, and keep a second property for the excluded class: canonicalisation still terminates and is deterministic there, even though isomorphic inputs may differ.
> 5. **If any consumer relies on the over-strong claim, list it in the PR.** That includes `apply --plan`'s diff, the report digest (ADR 0173), and entity export, which is planned. Say whether each one can meet the excluded case. Management declarations refuse blank nodes, so `apply` is probably safe. Check the rest.

## What was done

- The failing input of #140's `pipeline (devcontainer)` job (seed
  `1Axba7DbGjr2`, nine quads, IRI graph names only) was read by hand. The
  pair of leaves its two forms swap was reduced to five quads and four blank
  nodes: one dataset whose two quad orders get two forms in Varve.
- The same dataset was run through pyoxigraph 0.5.11 (a venv outside the
  repository) and dotNetRDF 3.5.2 (a file-based program outside the
  repository), 300 relabellings and orders each. Both give the same two forms
  byte for byte, so it is the algorithm's: §4.7.3 omits the graph of the
  relating quad. It is not #36's case.
- The property was narrowed to blank nodes in one graph, a second property
  covers every other dataset, and `rdf-canon.md` §6 and ADR 0059 (a dated
  amendment) say what holds. Each caller of `RdfCanonicaliser` in `src/` was
  read; §6.3 lists where each stands.

## What was decided by the session, and is open to the maintainer

- The class the converse is claimed in: every quad that mentions a blank node
  in one graph, no blank graph name. A wider class may hold; nothing here
  shows which.
- Adding §6.2's case to #36's report is left to the maintainer: the issue is
  not edited by this change.

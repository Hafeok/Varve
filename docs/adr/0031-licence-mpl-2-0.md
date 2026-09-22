# 0031 — Licence: MPL-2.0

## Status

Accepted. 2026-09-22. **Supersedes [0002](0002-licence.md).**

0002 keeps its text. The reasoning there was sound for the constraint it was
given; the constraint changed.

## Context

Varve adopts the Mind Over Machine open-source stewardship standard in full
(#16). One of that standard's principles is not a process item but a position on
what software is:

> Software is part of people's cultural heritage, not just companies'
> intellectual property. The systems and knowledge we develop are Open Source
> and CopyLeft.

Apache-2.0 is not copyleft. A downstream fork may take Varve, improve it, ship
the improvement as a binary and never publish a line of it. That is the outcome
Apache-2.0 permits by design and the outcome this principle exists to prevent.

**This departs from the brief.** `docs/brief.md` constraint 6 reads "Permissive
licence, no copied code", and ADR 0002 chose Apache-2.0 on exactly that
instruction. The brief is the authority and it is not edited by rule, so the
departure is recorded here rather than by quietly rewriting the sentence it
contradicts. The half of constraint 6 that says *no copied code* is untouched
and still binds: we read Oxigraph and dotNetRDF for behaviour and write our own.

The relicensing question has a deadline attached. It has to land before the
first prerelease is published, because a package that has been on nuget.org
under one licence cannot be recalled — versions can be unlisted, never deleted —
and anyone who took the Apache-2.0 version keeps those terms for that version
forever. Nothing is published yet (ADR 0029; the first tag is
`v0.1.0-preview.1`), so the window is open and will not reopen.

**Relicensing is possible at all only because there is no outside
contribution.** Every commit in this repository is the maintainer's, authored by
him or produced under his direction with AI assistance and committed in his
name. The copyright is held by one person, so one person can change the terms.
**After the first outside contribution this stops being true**: a licence change
then needs the consent of every contributor whose code is still present, which
in practice means either unanimous agreement or the removal of the dissenter's
work. That is the cost of getting this wrong now rather than a reason to defer
it.

## Decision

**MPL-2.0.** The full text is in `LICENSE`, verbatim, 373 lines, from the
canonical plain-text form.

MPL-2.0 is **file-level** copyleft, and that is the property being bought.
Reciprocity attaches per file: §1.4 defines Covered Software as the Source Code
Form "to which the initial Contributor has attached the notice in Exhibit A",
and §3.2 requires that modifications to those files be made available under this
licence. §3.3 then says the Larger Work — the consumer's application — may be
licensed under whatever terms the consumer likes. So:

- A fork that changes `TurtleReader.cs` must publish that file's source.
- A product that references `Varve.Turtle` from NuGet and writes its own code
  around it owes nothing and may stay closed.

That asymmetry is the whole point. The reciprocity binds the people who change
Varve, not the people who use it, which is what makes copyleft compatible with
being adopted as a set of libraries.

Three things follow mechanically, and each is enforced rather than remembered:

1. **Every `.cs` file carries the Exhibit A notice as its first three lines.**
   Under a file-level licence this is not decoration: a file without the notice
   is not plainly Covered Software, so a licence enforced only by a root
   `LICENSE` file erodes one new file at a time. `eng/licence-headers.cs` fails
   the build on a file that lacks it, with generated code excepted by pattern.
2. **`PackageLicenseExpression` is `MPL-2.0`** in `Directory.Build.targets`, an
   SPDX expression rather than a `licenseUrl` or a `licenseFile`, and
   `eng/package-metadata.cs` reads it back out of every built `.nupkg`.
3. **`LICENSE` and `NOTICE` are packed** into each package, and the metadata
   gate requires both to be physically present in the archive. Nothing in the
   nuspec points at them, so without a check nobody would notice their
   disappearance.

`NOTICE` keeps its name and gains a plain-language paragraph on what file-level
copyleft does and does not demand of a consumer. It is no longer an Apache-2.0
§4(d) obligation — MPL-2.0 has no equivalent requirement — but it is where a
reader looks, and deleting it to be pedantic would help nobody.

## Alternatives considered

- **Apache-2.0** — the previous choice, ADR 0002, and still the better licence
  on the two grounds that decided it: §3's patent grant with its termination
  clause, which matters more for a store and a query engine than for most
  software, and §4(b)'s notice-of-change requirement. Rejected because it is
  permissive, and permissive is the thing being reversed. **The patent exposure
  0002 identified does not disappear with this decision.** MPL-2.0 §2.1(b)
  grants patent rights over Contributions and §5.2 terminates the grant for a
  litigant, so the mechanism survives; it is narrower than Apache-2.0 §3 in
  scope, and that narrowing is a real cost of this change and is accepted
  knowingly rather than overlooked.
- **LGPL-3.0** — genuinely copyleft and well understood. Rejected on fit: its
  reciprocity is framed around linking and around the user's ability to relink
  a modified library, language written for C shared objects and awkward for
  .NET assemblies. It is worse still for the hosts we have to support — Native
  AOT and browser WebAssembly both produce a statically linked artifact, where
  "the user may substitute a modified version of the library" is a requirement
  with no obvious discharge. MPL-2.0's per-file test asks a question that has
  the same answer on all three hosts.
- **AGPL-3.0** — the strongest copyleft, and the only one that reaches a
  competitor who runs a modified Varve as a hosted service without distributing
  anything. Rejected **for the libraries**: AGPL's reciprocity extends to the
  combined work, so every consumer's application would become subject to it,
  which for a package meant to be picked up by other people's products is not a
  licence but a ban. **Noted as a possible later choice for the server image
  alone** — the server is a deployed application rather than a dependency, so
  the objection does not apply to it, and a split (AGPL-3.0 server image,
  MPL-2.0 libraries) is a coherent position to revisit at milestone 7. It is not
  decided here and this ADR does not pre-empt it.
- **Dual-licensing, copyleft plus a paid commercial exception.** Rejected: it
  requires a CLA taking assignment or broad rights from every contributor, which
  is precisely the arrangement the foundation's "not companies' intellectual
  property" principle rejects, and it makes the project's openness conditional
  on a business model.

## Consequences

- **The window closes at the first publication.** After `v0.1.0-preview.1` the
  licence of that version is fixed forever for anyone who fetched it.
- **The `System.Uri` and no-copied-code rules are unaffected.** MPL-2.0 is not
  one-way compatible with GPLv3 in the way Apache-2.0 was — it is explicitly
  compatible, via §1.12 Secondary Licenses, so a GPL project may combine with
  Varve. Nothing changes about what may come *in*: no code is copied from
  Oxigraph or dotNetRDF regardless of what their licences permit.
- **A per-file header is now a standing cost.** Every new `.cs` file carries
  three lines it did not before, and the first source generator this repository
  adds will meet the generated-code exception rather than the rule.
  `.editorconfig` carries `file_header_template` so an IDE inserts it; the gate,
  not the IDE, is what binds.
- **`eng/package-metadata.cs` changed in the same commit.** It compared the
  nuspec licence against the literal `Apache-2.0`, so relicensing without
  touching it would have turned CI red on the next pull request, which is the
  gate doing its job.
- **No revisit condition.** This is not a decision accepted ahead of evidence.
  The one open door is the AGPL-3.0 question for the server image, which is a
  different artifact and will be its own ADR if it happens.

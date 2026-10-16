# The store answers `TryGetTripleTermComponents`

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#87](https://github.com/Hafeok/Varve/issues/87); [#109](https://github.com/Hafeok/Varve/issues/109) from its plan |
| **Date** | 2026-10-15 |
| **Tool** | Claude Code 2.1.300, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01T3dL49Ra6rKiLDdGDgiu76` |
| **Branch** | `claude/sleepy-lovelace-fl8hn0`, from `main` at 9b964b8 (`v0.1.0-preview.6`) |
| **ADRs** | 0121 carries a dated amendment; none new |

## The prompt

The maintainer's first message, with the session prompt in a zip, extracted
outside the repository and never committed:

> @"/root/.claude/uploads/2d4f81d8-8bef-56dd-83d9-4b58c7fac4e8/59aa27e4-varve-issue-87.zip" Issue #87. The attached `varve-issue-87.zip` holds this session's prompt. Extract it into an empty directory outside the repository, then:
>
> 1. Read `varve-issue-87/varve-claude-code-issue-87.md` and issue #87 in full. The prompt governs this session; follow it as if I had pasted it here. Never commit the zip or the extracted folder.
> 2. Before planning, confirm: `v0.1.0-preview.5` is tagged on `main`, #87 is open, the twelve exemptions it names are in `baseline/exemptions.txt`, and the identity hook has run and signs as the agent. If any of these fails, stop and tell me which one.
> 3. Then post the short plan (every `IQuadSource` in `src/Varve.Store`, and whether each overrides), and wait for my approval.

The session prompt, `varve-claude-code-issue-87.md`:

> # Session: the store answers `TryGetTripleTermComponents` (#87)
>
> A small, read-side change in `Varve.Store`, due before SHACL (#13), which now depends on nothing else. Issue #87 is the specification: read it in full first, then ADR 0044 (blank node labels are never internalised back to handles), ADR 0121 (the method and its default), `src/Varve.Sparql.Evaluation/Operators/BgpOperator.cs` (`UnifyExternalised`), and the forwarding sources that already answer it (`GraphScopedQuadSource`, `QuadOverlay`, `Varve.Sparql.Store`'s `DefaultGraphView`). `AGENTS.md` applies in full.
>
> **Starting point.** Current `main`. Commit path (#91) has landed; Object storage (#92) and Client and integration part 2 (#93) run in parallel with this session. Neither touches the store's read views, but Object storage changes the default projection's storage: if it lands first, merge `main` and make sure every new `IQuadSource` it adds to `Varve.Store` answers the method too.
>
> ## Scope
>
> - Override `TryGetTripleTermComponents` on every `IQuadSource` in `Varve.Store` that a query or a validator can reach: `IndexSource`, `PendingSource` and `DatasetView` in `Views.cs`, `StagingView.cs`, and the bulk delta's sources in `Bulk/BulkDataset.cs` if a validator can match over them. List every `IQuadSource` implementation in `src/Varve.Store` in the plan, and say for each whether it overrides and why.
> - Each override is one dictionary-entry read: the store's dictionary already holds a triple term's components as handles. `[HotPath]`, no allocation, `false` for a handle that is not a triple term (including private terms, if erasure's classes exist by then: say what a private term's handle does here).
> - As-of reads: the components come from the dictionary, which is append-only, so a pinned view and an as-of view answer the same as the head. Say so in a test, not only in prose.
>
> ## Definition of done
>
> - The twelve lines (`sparql12/eval-triple-terms` `pattern-6`, `pattern-7`, `pattern-10`, `graphs-2`, each over the `store`, `scoped` and `protocol` subjects) leave `baseline/exemptions.txt` and enter `baseline/passing.txt`, and `eng/ratchet.cs` holds.
> - A store test: a triple term with a blank node component, matched at the head, through a pinned view and at an as-of position, with the components equal to the handles the store gave out.
> - An allocation test on the override, as for the forwarders.
> - ADR 0121's consequence on the store gets a dated amendment saying it is done, and the README's exemption paragraph is current.
>
> ## How this lands
>
> - One PR. No release descriptor: this is not a milestone. It is landed when its gates are green and approved, and ships in whichever release comes next.
> - Plan first, briefly (the list of sources is the plan), and wait for approval.
> - Commits reference #87 (`Closes #87` as a trailer on the last one), carry the DCO sign-off, are signed with the agent key, and never name a model.
> - A `docs/traceability/` record (ADR 0033).
> - No suppression or downgrade of DD or VARVE rules.

The session stopped at the first check: `v0.1.0-preview.5` was not a tag on
the remote then (the newest was `v0.1.0-preview.4`). The maintainer:

> preview.4 was meant; go ahead and post the plan

The plan listed `IndexSource`, `PendingSource`, `DatasetView` and
`StagingView` as overriding, the bulk delta's sources as `IndexSource`s, and
the key sources (`MergeSource`, `SectionSource`, `SpillKeySource`) as not
quad sources, and asked whether to fix a suspected bug in `PendingSource`.
The maintainer's approval:

> Approved, with three conditions and the bug answered.
>
> **Before anything else:** your message says `v0.1.0-preview.4` is on `main`; the check was `preview.5`. It is tagged (at cb36029), so I take that as a typo; confirm it in the record.
>
> ## Conditions
>
> 1. **The stack buffer's size is derived, not assumed.** "An entry too long for the buffer can't be a triple" holds only if the buffer is at least the longest possible triple entry. Derive it as a named constant from the entry encoding (the id, the kind byte, three ids at their maximum varint length), cite the storage-format section it comes from, and add a test that encodes a triple entry with three maximal ids and reads it back through `TryTripleParts`. If the encoding has no fixed maximum, say so and we decide.
> 2. **`PendingSource`'s lookup table is per commit, not per call.** Building it once per source is fine, but it is an allocation per commit that creates triple terms. Skip it entirely when the commit creates none, and keep the `[HotPath]` override itself allocation-free; the allocation test covers the override, and the record states the per-commit cost.
> 3. **The as-of test must cover the boundary.** It should include a handle the view cannot see yet: a triple term allocated after the pinned or as-of position answers `false` from that view, while the head answers it.
>
> ## The `PendingSource` bug: #109
>
> Fix it in this PR, as a separate commit that references #109 and ends with a `Closes #109` trailer. Write the failing test first: a commit creating a triple term, with and without a blank-node component, and a validator that externalises its handle through the pending source. It must get the triple term, equal to what the store returns after the commit. If the test doesn't fail, keep it, don't change the code, and close #109 with a note. Since SHACL's commit-time validation reads this exact source, this is the right moment to fix it.
>
> The rest of the plan stands: the ratchet, the ADR 0121 amendment, the README, the record, `Closes #87` on the last commit.

## The report

### The tag, confirmed

It was not a typo. At the session's first check, `git ls-remote --tags
origin` and the GitHub tag listing both ended at `v0.1.0-preview.4`
(2472e1a); `v0.1.0-preview.5` did not exist on the remote, and the session
stopped and said so. Re-checked after the approval, the remote holds
`v0.1.0-preview.5` at cb36029, as the maintainer says, and
`v0.1.0-preview.6` at 9b964b8, which `main` had moved to in the meantime: the
tag was cut between the two checks. The work starts from `main` at 9b964b8.
Nothing between cb36029 and 9b964b8 touches `src/Varve.Store` or
`src/Varve.Rdf`, and Object storage (#92) has not landed, so the list of
sources is the plan's.

### #109: not a bug, and the test stays

The test went in first, with and without a blank node inside the triple
term: a validator externalises the fresh triple term's handle through the
proposed state and must get the term the store returns after the commit. It
passed on unchanged `main`. The plan read `PendingSource`'s constructor,
`allocation.Term ?? BlankTerm(...)`, against `Resolver.Finalise`, where a
triple allocation is rebuilt with a null term; but `Finalise` passes each
final allocation through `Resolver.Materialise`, which fills a triple's term
before the pending source is built. The fallback only ever meets blank
nodes. No code changed; the test is its own commit, closing #109.

### The sources

| Source | Answers |
|---|---|
| `IndexSource` | From the dictionary at its position: `TermView.TryTripleParts`, over `TermDictionary.TryTripleParts` |
| `PendingSource` | A triple term the commit creates from the commit's allocation; any other from the head's `IndexSource` |
| `DatasetView` | Forwards to its source: an `IndexSource` for a head or pinned read, a `QuadOverlay` over one for an as-of read, which forwards |
| `StagingView` | A staged triple term from its staged parts; any other forwards to its `DatasetView` |
| `BulkDelta.Asserted`, `.Retracted`, and the proposed state a bulk validator reads | `IndexSource`s: answered above, no code |
| `MergeSource`, `SectionSource`, `SpillKeySource` | Key sources, not quad sources |

`TermDictionary.TryTripleParts` answers false without a read for a blank, an
inline or a private id (none names a triple term: a triple term is always
canonical, storage format §4.4) and for a canonical id past the view's
counter; otherwise it reads the one entry into a stack buffer and decodes
its kind and, for a triple, its three component ids, without a term.
**A private term's handle answers false**: the class is defined, nothing
allocates it before erasure mode, and it is never a triple term's id.

**Condition 1.** `LogFormat.MaxTripleEntryLength` is
`MaxUlebLength + 1 + 3 × MaxUlebLength` = 41: storage format §4.4's id, kind
byte and three component ids, each a `uleb` of at most ten bytes (§1). The
encoding has a fixed maximum. What a section can hold is 40: a canonical id is
below 2⁶², nine bytes, while a component can take ten (an inline id has its
class in the top two bits). The test encodes an entry with an id and three
components at `ulong.MaxValue`, 41 bytes, and reads it back with
`LogFormat.TryReadTripleEntry`; and builds a run whose section holds the
largest canonical id, 2⁶² − 1, over three ten-byte components, reads its
40-byte entry and takes it apart through `TryTripleParts`, which answers
false at a counter below it.

**Condition 2.** `PendingSource` builds a
`Dictionary<ulong, (ulong, ulong, ulong)>` in its constructor, once per
commit that runs validators **and** creates at least one triple term, and
not at all otherwise: one dictionary, its buckets and entries growing with
the number of triple terms the commit creates: an entry is 40 bytes (the key,
three component ids, a hash code and a chain link) and a bucket 4, before
the dictionary's growth to its next prime size. The override reads it and allocates nothing; measured,
in a validator, for a fresh and an existing triple term.

**Condition 3.** The store test commits a triple term with a blank node
inside at position 1, the blank node also a subject, and another triple term
at position 2. At the head and as of position 1 the first answers the
handles the store gave out, blank node first; the blank node, an IRI and a
literal answer false; the second answers at the head and false as of
position 1. It runs over the memtable and over a run flushed to `derived/`.
A second test pins at position 1, commits the second triple term, and shows
the pinned view answering the same as before and false for the new term.

### Allocation

Zero bytes over 1,000 calls, by `AllocationMeter`, for each of: the head and
an as-of view, a hit and a miss (a literal; a term past the position), over
the memtable and over a flushed run read through its blob; a staging view,
for a staged triple term and a forwarded one; and the pending source in a
validator, for a fresh and an existing triple term. `LogFormat.Reader`'s
constructor, which assigns three fields, is now `[HotPath]`, as `VARVE0003`
asked when `TryReadTripleEntry` called it.

### Conformance

The twelve lines left `baseline/exemptions.txt` with their comment, and the
ratchet reported exactly those twelve newly passing and nothing regressed;
they are in `baseline/passing.txt`, which holds **5,519** lines. The twelve
exemptions left are `http-rdf-update`'s. README's table, exemption paragraph
and count, and the roadmap's milestone 8 row and standing issues, say so.

### Not done, and why

- No new ADR or decision: the amendment records that ADR 0121's point 5 is
  done. The `PendingSource` and `StagingView` lookups cite the existing
  `StoreHotPathScope.TermLookupsAreHashLookups`.
- `PendingSource.TryInternalise` still does not find a triple term the
  commit creates by value, as before this change; a validator that has the
  handle takes it apart by handle, which is the path the evaluator uses.

## Addendum, 2026-10-16: the roadmap, #111

After the pull request was up, the maintainer added an item to it, with
the roadmap as of 2026-10-15 attached (`varve-roadmap-milestones.md`,
copied into the repository only as the prompt below says):

> @"/root/.claude/uploads/2d4f81d8-8bef-56dd-83d9-4b58c7fac4e8/08cac4c4-varve-roadmap-milestones.md" One more item for this PR, as its own commit: #111.
>
> The attached `varve-roadmap-milestones.md` is the maintainer's roadmap as of 2026-10-15. Replace the forward-looking part of `docs/roadmap.md` with it: keep milestones 1–7, Operability, 6b and Commit path as they stand (marked complete by their descriptors), and take everything else from the attachment, including the "After 1.0" section. Then run `eng/status.cs --check` and `--fixtures`; if a status check objects to a heading or a release number in the new text, fix the text, not the check, and say what you changed.
>
> The commit references #111 and ends with a `Closes #111` trailer. It touches only `docs/roadmap.md` (and the README only if `eng/status.cs` requires it). Keep closing keywords out of the PR description (#108).

The session took the attachment verbatim and kept the issue table and
*Not scheduled*, which it does not cover. The only edit was to make *At a
glance* a level-three heading, under the section's own. A first attempt kept
the old Web UI and Replication texts beside the new paragraphs and was
dropped, because they contradicted them. The status check and its six
fixtures passed with no change to the text. The session reported three
things in the new text without changing them: the Web UI and Replication
sections pointed to "attached" texts the file no longer held, #87 was still
shown as open, and the 1.0 section's base list and exclusions lived only in
#15. The pull request description named its issues with closing keywords in
prose; those were reworded, and the commit trailers close the issues.

The maintainer's reply, with a corrected attachment:

> @"/root/.claude/uploads/2d4f81d8-8bef-56dd-83d9-4b58c7fac4e8/948566a1-varve-roadmap-milestones.md" Thanks. You were right to keep the text unchanged; the text had three faults, now fixed in the attached `varve-roadmap-milestones.md`:
> 1. The Web UI and Replication sections no longer say "As the attached … text states"; they point to #96, #97 and the writer handover ADR.
> 2. #87 is shown as landed (SHACL's row, the standing issues and the SHACL section), since this PR closes it.
> 3. The 1.0 section carries #15's full "Must be true" and "Excluded from 1.0" lists again, verbatim from the issue. #15's own Solution puts the definition in `docs/roadmap.md`, so it must not live only in the issue.
>
> Replace your roadmap commit's content with this file, by the same rules: keep milestones 1–7, Operability, 6b, Commit path, the issue table and "Not scheduled" as they stand, and take the rest verbatim. Amend 5149972 or add a commit referencing #111; either way only the last roadmap commit carries `Closes #111`. Re-run `eng/status.cs --check` and `--fixtures`.
>
> Yes to the traceability addendum, as a separate commit with `Refs #111`.
>
> Rewording the PR description was right (#108).

The session amended 5149972, its own unmerged commit, into e4c1002, so
that one roadmap commit carries `Closes #111`. It then pushed the branch
with `--force-with-lease`. The forward-looking part is the corrected
attachment, verbatim apart from the same heading level; a diff against the
attachment shows that one line. `eng/status.cs --check` passes, and
`--fixtures` passes with all six fixtures failing for their own reason, so
no text was changed for the check.

# Management plane, part 2 — event schemas move to the management plane, and the close-out

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts below are
> verbatim. The transcript itself is held by the maintainer.

| | |
|---|---|
| **Issue** | [#105](https://github.com/Hafeok/Varve/issues/105), milestone Management plane, part 2 of 2; part 1 was #131 |
| **Date** | 2026-10-23 |
| **Tool** | Claude Code 2.1.309, a cloud session |
| **Model** | `claude-opus-5-5`, configured and served, from the session's own metadata |
| **Session identifier** | `session_01UsnuvR1VHdFMTHggiisiyE` |
| **Branch** | `claude/kind-pascal-u204tn`, from `main` at 59b230d |
| **ADRs** | 0167, filed unaccepted; 0144, 0145 and 0166 carry dated amendments of 2026-10-23 |
| **Release** | `v0.1.0-preview.13`, whose descriptor commit carries the closing trailer for #105 |

## The prompts

The maintainer's message, with two session prompts in a zip, of which this
session's is the first; the second, SHACL part 2b's, runs in parallel:

> @"/root/.claude/uploads/f012c578-6817-5011-842c-231099afe93a/7b91fc77-varve-part2-sessions.zip" Unzip the attached varve-part2-sessions.zip outside the repository and read varve-claude-code-management-plane-part2.md in full. It's the session prompt for Management plane part 2, closing #105, in github.com/Hafeok/Varve. Work from current main. AGENTS.md applies in full. The design is approved on #105 (part 1 plan, question 6); post a short plan and wait for approval only on anything that departs from it.

The session prompt, `varve-claude-code-management-plane-part2.md`:

> # Session: Management plane, part 2 — event schemas move to the management plane, and the close-out (#105)
>
> Part 1 (#131, PR #134, `v0.1.0-preview.10`) landed:
> - the planes, the fleet and its system dataset, and tier-1 followers;
> - write forwarding;
> - grants as code and `varve apply`;
> - placement;
> - the management position as a commit attachment (ADR 0165).
>
> This session lands the rest of #105 and closes it: the move of event-schema declarations, approved in the part 1 plan as its question 6, and the milestone close-out.
>
> **Starting point.** Current `main`. In parallel, **SHACL part 2b** switches the runtime validation of event and management declarations from code to shapes. See "Coordination" below.
>
> **Read before planning:**
> - #105, its amendments, and the part 1 plan on #105 (comment of 2026-10-19) with its revision. Question 6 is this session's design, as approved.
> - #134's traceability record;
> - ADRs 0163–0166 and 0168;
> - ADRs 0143–0147, above all 0144 (the event catalogue; point 6 is what moves) and 0145 (the schema registry, its revisions and golden files);
> - ADR 0157 (shapes for event declarations);
> - `Varve.Events` and `Varve.Management`.
>
> `AGENTS.md` applies in full. Plan briefly, since the design is approved, and wait for approval only on anything that departs from it.
>
> ## What to build (as approved; ADR 0167)
>
> 1. **The move, per dataset, as an explicit act.**
>    - `POST /manage/datasets/{d}/events/move`, and `varve manage move-events <d>`, by an operator or a dataset admin.
>    - **The freeze:** the system writer commits `varve:eventDeclarations varve:Moving` at m₁, and the dataset's writer refuses catalogue writes authorised at m₁ or later.
>    - **The copy:** the dataset's writer translates each `varve:events-catalogue/{enc(G)}` graph into nodes of `varve:manage/dataset/{d}` with `varve:sourceGraph G`. It submits them as a management write, **forwarded** to the system writer (ADR 0168, not `307`), conditional on the dataset's graph version.
>    - **The switch:** the system writer commits the copy with `varve:eventDeclarations varve:Moved` at m₂.
>    - **Recovery:** if the dataset's writer dies between m₁ and m₂, the move resumes from the frozen catalogue on the next writer. Test a crash at every step.
> 2. **The boundary is exact.** A data commit renders its events from the system dataset if and only if its recorded management position is m₂ or later. Otherwise it renders from the dataset's catalogue history, and commits from before part 1 record no position. This rests on the invariant from part 1: recorded management positions never decrease along a dataset's log.
> 3. **After the move:**
>    - writes to the catalogue graphs are refused with `409 event-catalogue-moved`, whose `manage` member names `/manage/datasets/{d}/events`;
>    - their history stays readable as of any position, under the same governor rule.
> 4. **The renderer:**
>    - `EventCatalogueHistory.ContinueFrom(...)` seeds the post-move history with the pre-move revisions, so a type whose bytes didn't change across the move keeps its revision and its `dataschema`;
>    - `ManagedEventDeclarations` in `Varve.Protocol` maps management commits to the catalogue-graph change form.
> 5. **Schema revisions after the move** (approved): `…/versions/{n}/revisions/m{p}`, with p the management position. Pre-move revisions keep their bare integers, and every URL already served stays immutable. Amend ADR 0145, with `SchemaRevision.Revision` becoming a `SchemaRevisionId` (breaking before 1.0). ADR 0145's golden files run unchanged, plus a new golden case, `moved`.
> 6. **Event declarations are validated in their new home** through the same entry point as before the move. See Coordination.
>
> ## Definition of done
>
> - **Rendering is byte-identical across the move.** 200 generated histories cross a move at a generated point, and every data commit is rendered three ways: from the dataset history without the move (the oracle), through the switched path, and replayed twice. Commits whose declarations didn't change after the move render identical bytes in all three. #93's two rendering properties are re-run over the switched path.
> - **Refusals:** a catalogue write after m₁ is refused, at the store level and over HTTP.
> - **History:** as-of reads of the catalogue graphs equal those before the move.
> - **Crash recovery:** a crash at every step of the move recovers to a completed move, with nothing lost or doubled.
> - **The fleet test** on Azurite and RustFS includes a move, with the request sent to a non-writer and forwarded.
> - **The close-out of #105:**
>   - README Status, `docs/roadmap.md` (Management plane complete), AGENTS.md's State, `eng/status.cs` and the changelog;
>   - the operator guide: a "moving event schemas" page, and `fleet.md` updated;
>   - `docs/problems/` (`event-catalogue-moved`);
>   - the traceability record.
>
> ## Coordination with SHACL part 2b
>
> - **SHACL part 2b swaps the runtime validator** for event and management declarations from code to shapes, behind the existing entry points (`DeclarationValidator`, `EventDeclarationCheck`). Call those entry points, never a concrete code validator, so the switch is transparent to the move.
> - **ADR 0157's shapes must validate event nodes in both homes.** If SHACL 2b lands first, add your post-move fixtures to its agreement property. If you land first, leave a test fixture of moved declarations that it can pick up.
> - **Whichever of the two lands second merges `main`,** re-runs both agreement properties, and reconciles `shapes/management.ttl` with any vocabulary terms the move adds (`varve:eventDeclarations`, `varve:Moving`, `varve:Moved`, `varve:sourceGraph`).
>
> ## How this lands
>
> - **One PR,** red only on `CS0618`.
> - **The descriptor** is at the next free `0.1.0-preview.N` when it lands (ADR 0102). It carries `accepted-by: mailto:emil@okkels-klein.dk` and `Closes #105` as a trailer on the descriptor commit, and names #105 and #131. Use bare #N elsewhere, and no closing keyword in the PR description (#108).
> - **The ADR is 0167,** reserved for this. Amend 0144, 0145 and 0166.
> - **Commits:**
>   - signed with the agent SSH key, as `Claude (Emil) <claude+emil@okkels-klein.dk>`, the repository's rule regardless of any generic hook;
>   - reference #105, with the DCO sign-off;
>   - never name a model.
> - **A `docs/traceability/` record** (ADR 0033).
> - **No suppression or downgrade of DD or VARVE rules.**
> - **CI images** only from `.github/ci-images.yaml`.
> - **Test time:** size land-gate iteration counts, report each new test's duration per OS (#126), and put long runs behind environment variables that the nightly workflow sets.
> - **Land through `land/`.**

## The plan

Posted on #105 on 2026-10-23
([comment](https://github.com/Hafeok/Varve/issues/105#issuecomment-6362900052)).
It builds question 6 of the part 1 plan as approved, and names one point
that needs a decision and two additions:

- **A, the credential of the copy.** ADR 0168 forwards only a caller's own
  request, under the caller's token, with no peer identity. A dataset's
  writer that is not the system writer has no token of its own to send the
  copy with. The proposal routes the move to the dataset's writer, which
  makes the freeze and the switch as management writes under the caller's
  token, forwarded once to the system writer. The move is idempotent and
  resumable: by itself when the instance also writes the system dataset,
  otherwise on the next request.
- **B, additions.** A cancel (`Moving` back to none), which the writer makes
  itself when the catalogue does not translate one-to-one; the transitions
  the validator allows; the views at the head read the applied position.
- **C, reported.** No `DeclarationValidator` type exists on `main`; the code
  validator is `DeclarationReader` inside `ManagementValidator`, and the
  session calls only those entry points.

The session built point A as proposed while waiting. **No answer had come
when this record was written**; point A is the question the pull request
carries.

## The report

**Built.**

- **ADR 0167** and its decision set, filed unaccepted; dated amendments to
  0144 (point 6 realised), 0145 (revision identifiers) and 0166 (the four
  terms; event nodes in a dataset's graph).
- **`Varve.Events`.** `SchemaRevision.Revision` is a `SchemaRevisionId`, a
  dataset position or a management position written `m{p}` (breaking).
  `EventCatalogueHistory.ContinueFrom` seeds the history after the move with
  the revisions at it. The golden case `moved`.
- **`Varve.Management`.** `varve:eventDeclarations` (`varve:Moving`,
  `varve:Moved`) read into `DatasetDeclaration.EventDeclarations`. ADR
  0144's terms are allowed in a dataset's graph that says `varve:Moved`, and
  refused elsewhere. `EventDeclarationMoves` allows the transitions.
- **`Varve.Protocol`.**
  - `ManagedEventDeclarations`: a dataset graph's event nodes in the
    catalogue graphs' form and back, and the diff of two states.
  - The boundary in the renderer and the views (`EventCatalogues`).
  - `EventCatalogueFreeze` and `409 event-catalogue-moved`.
  - `ManagementValidator` composed with `EventDeclarationCheck` over the
    translation.
  - `GET /manage/datasets/{d}/events` and `POST …/events/move` with the
    driver: freeze, barrier, copy, switch, and the cancel.
- **`Varve.Server`.** The freeze among a write's validators. The dataset
  validator refuses a catalogue write once the applied declarations say
  `Moving` or `Moved`. A `Moving` move is resumed on the interval by an
  instance that writes both. Readiness names an unfinished move.
  `varve manage move-events`.
- **The close-out:**
  - README Status, `docs/roadmap.md` (Management plane *(complete)*),
    AGENTS.md's State, the changelog and the descriptor;
  - the operator guide's "Moving event schemas" page and `fleet.md`;
  - `docs/problems/event-catalogue-moved.md`, `docs/vocabulary.md`,
    `docs/spec/events.md` §3.5 and §6.2.

**The definition of done, and where each part is.**

| Gate | Test |
|---|---|
| rendering byte-identical across the move, three ways | `MovePropertyTests.rendering_is_byte_identical_across_the_move`, 200 generated histories crossing a move at a generated point: the oracle (the dataset history without the move), the switched path, replayed; equal bytes where the declarations did not change after the move, and the same event with an `m{p}` or unchanged `dataschema` where they did |
| #93's two rendering properties over the switched path | `MovePropertyTests`: every commit twice (200), the declaration in force at its position (50) |
| ADR 0145's golden files unchanged, plus `moved` | `GoldenSchemaTests` |
| refusals at the store and over HTTP | `EventMoveTests`, `ProblemCatalogueTests`, `FleetTests` |
| as-of reads of the catalogue graphs equal before the move | `EventMoveTests` |
| a crash at every step recovers, nothing lost or doubled | `EventMoveTests.A_move_stopped_at_any_step_…`: stopped before the freeze, after it (resumed on the next request, and by itself), and after the switch; compared with an unstopped move's declarations and the events before it |
| the fleet, the move sent to a non-writer and forwarded | `FleetTests.Event_declarations_move_through_a_non_writer_…`, with the dataset on the system writer and placed on the other instance, two processes on Azurite |
| the translation one-to-one | `MovePropertyTests.the_copy_translates_…` (200), `ManagedDeclarationTests` |
| a fixture of moved declarations for SHACL part 2b | `tests/Varve.Events.Tests/fixtures/moved-declarations/`; `MovedFixtureTests` shows ADR 0157's shapes conform on it in both homes |

Mutation check: the property fails when `ContinueFrom` does not seed the
revisions.

**Deviations, stated.**

- **RustFS.** As in part 1, the server reaches an S3 store only by web
  identity (ADR 0137), which RustFS in CI has not. The fleet's server
  processes, the move among them, run on Azurite. The move is above the store.
- **The catalogue's refusal.** The freeze is exact at the authorised
  position, by a request validator. The dataset validator also refuses a
  catalogue write once the applied declarations say `Moving` or `Moved`, so
  that a malformed write is `event-catalogue-moved` and not
  `event-declaration-invalid`. That refuses more, never less.
- **Point A** is built as proposed and awaits the maintainer.

**Durations, Linux x64 (the session's container).** Recorded from the
session's own runs, Azurite started locally; other operating systems are
CI's.

| Project | Tests | Duration |
|---|---:|---:|
| `Varve.Events.Tests` | 127 | 11 s |
| `Varve.Management.Tests` | 45 | 6 s |
| `Varve.Protocol.Tests` | 113 | 49 s |
| `Varve.Protocol.Client.Tests` | 117 | 12 s |
| `Varve.Server.Tests`, with `FleetTests` on Azurite | 127 (6 skipped: other providers) | 37 s |

The new tests: `MovePropertyTests` 0.8 s in all (the 200-history property
0.5 s); `EventMoveTests` 1.6 s; the two fleet move cases 2.6 s and 3.0 s.
None needs a long-run variable.

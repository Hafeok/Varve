# The learning track, part (b): chapters 3 to 6, the generated portfolio

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompts are verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#411](https://github.com/Hafeok/Varve/issues/411), part (b) of [#94](https://github.com/Hafeok/Varve/issues/94) |
| **Date** | 2026-11-25 |
| **Tool** | Claude Code, a cloud session started from the desktop app |
| **Model** | `claude-opus-5-5`, configured, from the session's own metadata |
| **Session identifier** | `session_01NbWSYS9EfnKVVJ6zaNkU5z` |
| **Branch** | `claude/learn-b-94`, from `main` at 36e77d45, after train 4 (#379) landed |

## The prompts

The session prompt of 2026-11-19 and its rulings are recorded with part (a),
in [`2026-11-19-issue-364-learning-track-part-a.md`](2026-11-19-issue-364-learning-track-part-a.md).
Part (b) was started by:

> For the learning track session (#94).
>
> Part (a) is accepted as landed. Go for part (b), chapters 3 to 6, from
> `main` after train 4 has landed (it carries the docs site design,
> #379), on a new sub-issue and branch `claude/learn-b-94`.
>
> 1. Post the plan for chapters 3 to 6 on the sub-issue first: for each,
>    the SQL it is set against, the one question it answers, the Varve
>    API or route the sample calls, and its markers (erasure, #203, in
>    chapter 5). Wait for the maintainer's answer before writing.
> 2. Chapter 3's commit refused by the REC 4 shapes: show the refusal and
>    the report exactly as the release returns them.
> 3. If the new site layout needs anything different in the Markdown of
>    chapters 1 and 2, say what; do not change them in part (b) without
>    saying so.
> 4. For chapter 7, post on #94 now the REC terms you propose for alarms
>    and actuation commands, each with the file and line of the pinned
>    REC 4 source it comes from, and what you would do where REC has no
>    term. The maintainer confirms them before part (c).
> 5. A run that stalls in the Inference tests is #399: cancel after 15
>    minutes, note it there, and report the other jobs.

The plan is #411's description. The answer to it:

> Plan on #411 accepted as proposed, go ahead with part (b)

## The report

- **Issues.** The plan is #411. Chapter 7's proposed terms are on #94,
  waiting for the maintainer's confirmation before part (c). #414 is
  opened: the generator types assets `rec:Cabinet`, which REC 4 makes a
  room.
- **The generated portfolio.**
  `tests/Varve.Rec.Tests/LearnFixtureTests.cs` writes
  `samples/learn/data/03-portfolio.trig`: REC S at its head, seed
  20261015, cut to buildings 0 and 1 of site 0, flattened into the default
  graph. 492,272 bytes, 4,844 subjects (1 region, 1 site, 2 buildings, 16
  levels, 400 rooms, 1,200 assets, 3,215 points), pinned at SHA-256
  `6d2d5ec3…0e0e`. Through chapter 3's gate it loads in about 7 s. The
  generator and its sizes are unchanged.
- **Chapter 3, schema without migrations.**
  - The portfolio's 27 types read from the data; building 1's point types;
    its sensors through REC's hierarchy, read from the graph `:rec` with
    `rdfs:subClassOf*`; a `brick:CO2_Level_Sensor` added and counted by
    the same query.
  - REC 4 bound as a `reject` shapes binding over the default graph.
    Chapter 1's mistake, on room 2.01's typed capacity, is refused with
    `Rejected`, no reason, and two report terms: the problem type and an
    `rdf:JSON` literal with two findings, `DatatypeConstraintComponent` and
    `MaxCountConstraintComponent`. The page quotes both terms as the store
    returned them, then a findings table the sample formats.
  - Chapter 1's own update, room 9.99, is refused too, for an untyped
    capacity (`ClassConstraintComponent`), not for `"ten"`: a shape targets
    nodes by type. The page says so.
  - REC is loaded as one commit. Loaded file by file, the Brick commit is
    refused: Brick's SPARQL constraints need the composition's prefixes, and
    a bound shapes graph that does not load refuses the commit that made it
    so. That is the gate working as ADR 0169 says, not a defect.
- **Chapter 4, commits.** Room 3.02 split in one request of three
  operations. Computing half the seats as `?seats / 2` gives an
  `xsd:decimal`, which REC refuses, and none of the request lands; the cast
  fixes it. Two writers on one position: the first commits, the second gets
  `Conflict`, re-reads, and does not retry.
- **Chapter 5, deletion and history.** `DELETE WHERE` on the subject leaves
  room 3.01's link dangling; the links are deleted next; both again are
  `NoChange`; `AsOfAsync` reads the sensor before it was retired. Marker:
  `<!-- unshipped: #203 -->`, erasure.
- **Chapter 6, temporal and audit.** `AsOfTimestampAsync` for level 3 on
  1 February, `DiffAsync` for the renovation, and `ReadMetadataAsync` for
  who moved the display, on a clock the sample sets by hand. The page says
  Varve has transaction time only.
- **Shared code.** `Shared/Load.cs` gains `TurtleIntoAsync` beside the
  `load` region chapter 1 quotes, which is unchanged. `Shared/Rec/` holds the
  gated dataset and the findings table, compiled into the samples that set
  `VarveLearnRec`. `Show` gains the portfolio's prefixes.
- **Records.**
  - ADR 0141, amended and filed unaccepted: the slice, the portfolio as a
    pinned cut, the default graph until chapter 9, and REC's graph as the
    one exception. Ruling `LearningTrackDataIsTheSliceAndAPinnedCut`.
  - ADR 0249, amended and filed unaccepted: a sample's state leaves out the
    pinned vocabulary's graph, since with it chapter 3's state is 10 MB, 8 of
    them REC. Ruling `StateLeavesOutThePinnedVocabulary`. Chapter 3's state
    is 1.8 MB.
- **Beside chapters 1 and 2.** Their Markdown is unchanged. The index lists
  chapters 3 to 6 and moves the term table's link below the chapter list,
  and the site's lede for the section reads "RDF and SPARQL for SQL
  developers, with Varve, chapter by chapter, on one RealEstateCore
  portfolio." (`eng/site/SiteMap.cs`). Chapter 2's last paragraph said
  chapter 3 loads "many buildings" and adds "a new kind of asset"; it loads
  two and adds a kind of sensor, which is a point. Changed on 2026-11-26, at
  the session user's request.
- **The release.** Built against the published `0.1.0-preview.22` packages
  (`eng/learn.cs -- --run --packages 0.1.0-preview.22`), all six samples
  print what `expected/` holds, chapter 3's refusal and report included, so
  chapters 3 to 6 show only what that release does. Published trimmed for
  linux-x64, all six print the same.
- **Durations, measured in this container (Linux x64).** `learn --run`, six
  samples, after the first build: chapter 3 about 13 s, chapter 4 about 7 s,
  chapters 5 and 6 about 4 s each.
- **Nothing in `src/` changed.**

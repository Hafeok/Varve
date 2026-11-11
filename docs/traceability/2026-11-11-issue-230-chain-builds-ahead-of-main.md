# Chain builds ahead of main: the ADR (step 0)

> **Recorded contemporaneously**, by the session that did the work, under
> [ADR 0033](../adr/0033-commit-traceability.md). The prompt is verbatim. The
> transcript itself is held by the maintainer.

| | |
|---|---|
| **Issues** | [#230](https://github.com/Hafeok/Varve/issues/230); [#277](https://github.com/Hafeok/Varve/issues/277) (step 1) and [#278](https://github.com/Hafeok/Varve/issues/278) (found: ADR 0241 taken twice) opened |
| **Date** | 2026-11-11 |
| **Tool** | Claude Code 2.1.322, a cloud session. Not the maintainer's delegate (ADR 0239 point 1) |
| **Model** | *Left for the maintainer.* This tool's configuration keeps model identifiers out of every file it pushes, so the maintainer fills it in from the session's metadata (ADR 0033, ADR 0241 point 3). |
| **Session identifier** | `session_01NbWSYNzDrbT3ENstGnmH3N` |
| **Branch** | `claude/nice-mendel-495j4y`, on #261's head at a3578a2 |
| **ADRs** | [0243](../adr/0243-chain-builds-ahead-of-main.md) filed unaccepted; [0088](../adr/0088-only-checked-commits-reach-main.md) and [0102](../adr/0102-a-release-is-a-descriptor.md) amended, unaccepted |

## The prompt

The message:

> Read AGENTS.md, then the session prompt in the attached varve-claude-code-chain-builds.md. The decision and my review are on #230. Start with step 0 (the ADR) and step 1 (landing trains), and post your plan on each issue before building.

The session prompt, attached:

> # Session prompt: chain builds ahead of `main` (#230)
>
> Paste into a fresh session on `Hafeok/varve`. Read `AGENTS.md` first and follow it, including the issue-log practice: comment on each issue when you start it, post your plan there before building, and file found work as issues when you find it.
>
> The decision is #230, accepted by the maintainer on 2026-11-06 as written. This prompt adds the rulings from the review of 2026-11-11 (also posted on #230) and an order of work. Where this prompt and #230 differ, this prompt is the later ruling; record each difference in the ADR.
>
> You are a cloud session, so you are not the maintainer's delegate (ADR 0239). File every decision unaccepted, post your heads, and ask. You can push branches of any name; you cannot delete branches or push tags, so anything that tags or deletes is a workflow.
>
> ## What this is for
>
> Two different problems, and the steps are ordered by which one hurts now:
>
> 1. **Landing is serial.** Each landing takes one full CI run (about 70 minutes), and moves `main`, so every other open pull request falls behind and needs a merge and another run. On 2026-11-10 twelve pull requests waited in line. #230 as written does not fix this; step 1 does.
> 2. **Work can't build on unlanded work, and nobody can try a feature before it lands.** That is #230 proper: the feature tree, stable tags, and chain builds.
>
> ## Rulings from the review
>
> **The open questions in #230, settled:**
> 1. **Unstable-build marker: a reserved header byte, not a marker file.** A backup is a copy of the log, and a marker file beside `log/` is lost in exactly that copy. A header byte travels with the data, and released readers already refuse a non-zero reserved byte (`docs/spec/storage-format.md` §1), so every release ever cut refuses it too.
>    - A chain build sets the byte in every file header it writes.
>    - A chain build opens for writing only a store that already carries the byte, or creates a new one. It never writes into a release-written store, because it can't mark headers that already exist.
>    - It is a format change, so it goes through the format inventory (ADR 0216).
> 2. **A stable tag is cut by a workflow, on request, never by hand and never on every green head.** The request names a head (a `workflow_dispatch`); the workflow verifies every gate is green on that exact sha, then tags. A human adds nothing to "every gate is green", and tagging every green head produces tags nobody asked for.
> 3. **Chain builds publish `.nupkg` files and their SBOMs only.** No GHCR image and no WASM bundle under a chain version in this ADR. Add them by dated amendment when someone needs one.
> 4. **Vocabulary namespace:** `https://w3id.org/varve/chain#` for now. When the gate moves to how-we-work it takes a namespace of its own, and the move is a rename recorded then. (The maintainer may overrule this on #230 before step 2.)
>
> **Added by the review:**
> - **Landing trains (step 1).** Not in #230 as written. It is what removes the serial cost.
> - **The Varve version the gate runs on is pinned,** in a lockfile, and bumped like any dependency. "The latest release" floats, and a floating gate can't be reproduced (ADR 0224's locked restore applies to it like everything else).
> - **Trial merges are bounded.** Every non-ancestor pair gets a `git merge-tree` check, which costs seconds. A build and test in a scratch tree runs only for pairs whose declared `touches` overlap, or whose merge-tree result changes the same project. One at a time, and never blocking a branch's own CI. The runner queue is already the constraint.
> - **Stable implies the branch's own decisions are accepted.** An unaccepted decision fails the build (`CS0618`), so a branch with one is never green. Say so in the ADR; the explicit check "the parent's decision set is accepted at the forked tag" stays.
> - **Chain branches never land by squash.** A stable tag must stay reachable from `main` after landing, and a child is forked from its parent's commits. So the landing path for a chain branch is a fast-forward or a merge commit. This constrains the planned gh-tt delivery (#243's probe): if `gh tt deliver` replaces `land/`, it must not squash a chain branch. Record it as a constraint on that future ADR.
> - **ADR numbers assigned at landing** is a piece of work of its own (step 6). Until it lands, a session takes the next free number and checks every open pull request for a collision, as today.
> - **Chain branches are opt-in.** Independent work stays on its session branch with a pull request to `main`. Nothing in flight is migrated.
>
> ## Steps
>
> Each step is one pull request under its own issue, in this order. Steps 1 and 2 are independent of each other. Post the plan for a step on its issue before building it.
>
> ### Step 0 — the ADR
>
> File the ADR from #230's text, with the rulings above folded in and each one marked as settled in review on 2026-11-11. Unaccepted. Dated notes on ADR 0088 (landing by fast-forward or merge commit; trains) and ADR 0102 (chain builds are out of a release's scope; the release workflow refuses `chain/*`). `Closes #230` when it lands. Steps 1 to 6 cite its rulings.
>
> ### Step 1 — landing trains
>
> Open an issue: "Landing trains: several approved heads land in one run".
>
> - **A train** is one head that merges two or more pull request heads, in a stated order, onto the current `main`.
> - **Members:** each member's own head is approved for its exact sha and was green on its own. The train's merge commits carry `Landing-only: merge` and are reproducible by `git merge-tree`; a merge that needed a hand resolution disqualifies the train.
> - **One run:** every gate runs once, on the train's head. Green fast-forwards `main` to it.
> - **Releases:** a train carries at most one release descriptor, and that member is last. The release is cut from the train's head as today.
> - **Failure:** a red train lands nothing. `land.cs` prints the train without its last member, so the lander can retry shorter. Members stay individually landable.
> - **Closings:** #209's check runs per member. Every member's `Completes:` issues need their trailers, and after the fast-forward every one is checked closed.
> - **Code:** `eng/land.cs -- --train N1 N2 …` builds and pushes the train head; the existing approval check runs per member. Fixtures: a clean two-member train; a member whose approval is for another sha (refused); a train with two descriptors (refused); a merge that conflicts (refused).
> - **Docs:** `AGENTS.md`'s landing section, `docs/releases.md`.
>
> This step pays back at once. Do it first.
>
> ### Step 2 — the chain graph and its gate
>
> Issue: "Chain branches: the feature tree, declared and checked".
>
> - `.chain/feature.ttl` on each `chain/<feature>` branch: its one parent (`main` at a release, or a chain branch at a stable tag) and its `touches`.
> - The graph is derived on every run from those files plus git facts, and is never a source of truth.
> - The rules are a SHACL shapes graph, with SPARQL-based constraints for depth (backstop 3), diamonds and acceptance, validated by `Varve.Shacl` and `Varve.Shacl.Sparql` at a **pinned** released version.
> - The checks are #230's list: one declared parent; the fork point is a parent stable tag; `main` is an ancestor; no diamond; the parent's decision set accepted at the forked tag; depth within the backstop; existing chain tags reachable; no rebase or force-push (the previous head is an ancestor of the new one).
> - A violating and a conforming fixture graph per shape, as for an analyzer rule.
> - The gate lives under `tools/` beside repo-standard, written so it can move to how-we-work: no Varve-specific path or name in its code, only in its configuration.
>
> ### Step 3 — the unstable-build marker
>
> Issue: "Storage format: a store written by a chain build is refused by a release build". Ruling 1 above. The format inventory entry (ADR 0216), `docs/spec/storage-format.md`, a test that a release build refuses a marked store with the existing reserved-byte refusal, and a test that a chain build refuses to write into an unmarked one. This lands before any chain build is published.
>
> ### Step 4 — stable tags and chain builds
>
> Issue: "Chain builds: stable tags, path-encoded versions, packages as a prerelease".
>
> - The tagging workflow of ruling 2.
> - Versions as #230 has them: `<base>.chain.<feature>.<build>[.<feature>.<build>]…`, read from the tag `chain/<version>`. Test the ordering against SemVer 2.0 §11 and against NuGet's comparer: a chain build sorts above its base preview and below the next one; a descendant sorts above its parent. Feature names are restricted to `[0-9a-z-]` with no leading zero in a numeric identifier; the gate refuses anything else.
> - A chain-build workflow that builds from a stable tag only, publishes `.nupkg` files and SBOMs as assets of a GitHub Release marked prerelease, and never pushes to nuget.org. It refuses a commit that is not a chain stable tag. The release workflow refuses `chain/*`.
> - The build sets the marker of step 3 because its version carries `.chain.`.
> - `docs/` for a consumer: how to restore a chain build from a GitHub Release.
>
> ### Step 5 — interference by trial merge
>
> Issue: "Chain branches: interference between siblings found by trial merge". As bounded above. A recorded interference blocks landing of the later of the two until one merges the other's parent forward or re-parents. It is also the evidence a landing train of siblings rests on, so step 1's `--train` consults it when both members are chain branches.
>
> ### Step 6 — allocators designed out
>
> Issue: "ADR numbers assigned at landing; `*.Unshipped.txt` merge by union".
>
> - On a branch, an ADR file is named by its decision set's key and carries no number; text cites keys. `land.cs` assigns the next free number at landing, renames the file and rewrites the references, in a commit marked `Landing-only: renumber` that it can reproduce.
> - `.gitattributes`: `merge=union` for `*.Unshipped.txt`.
> - The contributing guide, the session prompt template and the issue template (`parent`, `touches`).
>
> ## Not in this work
>
> - Migrating open pull requests to chain branches.
> - Replacing `land/` with gh-tt. That is its own ADR, and it inherits the no-squash constraint.
> - Agent grants (#246). Where a step needs "who may tag" or "who may land a train", it uses today's rule (ADR 0239) and says where #246 would change it.
>
> ## Close-out
>
> `docs/sessions/2026-11-<name>.md` and a `docs/traceability/` record per issue: pull requests, the ADR number taken, issues opened and closed, test counts before and after, questions, and anything deferred as a linked issue.

## The report

- **The issue log.** The prompt says AGENTS.md has the issue-log practice. On
  `main` it does not yet; it is in #259 (ADR 0241's pull request), which this
  branch stacks on. The session followed it: it commented on #230 when it
  started and posted the plan there, opened #277 for step 1 and posted that
  plan there, and filed #278 when it found ADR 0241 taken by two open pull
  requests.
- **Base.** The branch forks from #261's head, not from `main`. This ADR adds
  a dated note to 0102 after #261's amendment of 2026-11-10, and step 1
  extends #260's closing-trailer check. When #259–#261 land, the branch
  merges `main` forward.
- **Number 0243.** The session checked every open pull request: 0240 is #268's,
  0241 is #259's and also #264's (#278), 0242 is #267's.
- **The ADR** is #230's text, with the review's four settled questions and
  seven additions folded in. Each is marked *settled in review, 2026-11-11*,
  and a table, **Differences from #230**, lists every change from the issue's
  text. It has thirteen keys, all unaccepted.
- **Two things the session added**, for the maintainer to keep or strike:
  - an alternative, GitHub's merge queue in place of trains, with the reason
    it was not chosen;
  - a revisit condition on trains. The ADR is accepted ahead of the evidence
    that trains are faster, so it revisits if more than half of the first ten
    trains land shorter, or the median train takes longer than two serial
    landings.
- **The amendments.**
  - 0088: `main` moves only by fast-forward, a merge commit on a branch is a
    commit like any other, a chain branch never lands by squash, and a train
    is one checked head.
  - 0102: chain builds are outside a release's scope, the release workflow
    refuses `chain/*`, and a release may ride last in a train.
- **Checks run:** `decision-sets` passes (220 sets, 1,528 decisions). No code
  changes; test counts are unchanged.
- **Questions, posted on #230:**
  1. Should #230 close when this lands, with the rulings still unaccepted, or
     should this wait for acceptance?
  2. Do you overrule the namespace before step 2?

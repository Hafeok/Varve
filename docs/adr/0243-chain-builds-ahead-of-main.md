# 0243 — Chain builds ahead of main, and landing trains

## Status

**Accepted — filed unaccepted by the session for #230, 2026-11-11** (ADR 0066),
its thirteen rulings accepted on #280 on 2026-11-12, at the maintainer's word,
by his delegate (ADR 0239). Decided
by the maintainer on [#230](https://github.com/Hafeok/Varve/issues/230),
accepted there as written on 2026-11-06; the review of 2026-11-11, posted on
the same issue, settled its four open questions and added seven rulings.
Every ruling the review settled or added is marked *settled in review,
2026-11-11*. Where the review and #230's text differ, the review is the later
ruling, and **Differences from #230** below lists each one. Acceptance is the
maintainer's act, or his delegate's for a decision he has read (ADR 0239).

**Amends [0088](0088-only-checked-commits-reach-main.md)** (landing by
fast-forward or merge commit; landing trains) and
**[0102](0102-a-release-is-a-descriptor.md)** (chain builds are outside a
release's scope; the release workflow refuses `chain/*`), each by a dated
amendment in its own file. Supersedes nothing. Records decision set
[`chain-builds-ahead-of-main`](../decisions/chain-builds-ahead-of-main.md).

**Built in steps**, one pull request and one issue each, in this order:

| Step | Issue | What |
|---:|---|---|
| 0 | #230 | this ADR |
| 1 | #277 | landing trains |
| 2 | — | the chain graph and its gate |
| 3 | — | the unstable-build marker |
| 4 | — | stable tags and chain builds |
| 5 | — | interference by trial merge |
| 6 | — | allocators designed out |

Steps 1 and 2 are independent of each other. Step 3 lands before any chain
build is published. Each step cites this ADR's keys.

## Context

**Landing is serial.** Each landing takes one full CI run, about 70 minutes,
and moves `main`, so every other open pull request falls behind and needs a
merge from `main` and another run before it can land. On 2026-11-10 twelve
pull requests waited in that line. ADR 0239's delegate made the landing faster
to carry out, not shorter.

**Work cannot build on unlanded work, and nobody can try a feature before it
lands.** A session starts only after the one before it lands, so independent
work queues behind unrelated work, and dependent work cannot be consumed until
it is on `main`. The sessions of #257 (#259, #260, #261) already stack branch
on branch by hand, with nothing to say what a branch rests on or whether that
is safe.

#230 addresses the second problem. It does not address the first: chain
branches still land one at a time. The review added landing trains for that.

## Decision

### 1. A feature tree rooted at `main`

- **Unlanded features are `chain/<feature>` branches.** Each one declares
  exactly one parent: `main` at a release, or another chain branch at one of
  its stable tags.
- Branches may diverge but never join: **no diamonds**. Independent features
  are siblings, so only a feature's descendants carry its risk.
- **Chain branches are opt-in** *(settled in review, 2026-11-11)*.
  Independent work stays on its session branch with a pull request to `main`.
  Nothing in flight is migrated.

### 2. Accepted decisions gate stacking

- **A feature may have children only once its whole decision set is
  accepted** at the stable tag the child forks from. Descendants then rest on
  settled design, and rejecting an ancestor at review stays cheap.
- **Stable implies the branch's own decisions are accepted** *(settled in
  review, 2026-11-11)*. An unaccepted decision fails the build (`CS0618`, ADR
  0066), so a branch with one is never green and never gets a stable tag. The
  gate still checks explicitly that the parent's decision set is accepted at
  the forked tag, because the parent's decisions are the ones the child relies
  on.
- **Depth has a backstop**, 3 for Varve, changeable only by dated amendment.
- Once accepted, a parent's decisions change only by supersession, which its
  children receive by merging forward.

### 3. Stable points, and who tags them

- **A point is stable when every gate is green on its head.** A stable point
  gets an immutable tag. Only a stable point can be forked from or built into
  packages.
- **A stable tag is cut by a workflow, on request** *(settled in review,
  2026-11-11; open question 2)*. The request is a `workflow_dispatch` that
  names a head. The workflow verifies that every gate is green on that exact
  sha, then tags. It is never cut by hand and never on every green head: a
  human adds nothing to "every gate is green", and tagging every green head
  makes tags nobody asked for.
- Who may request one is today's rule, ADR 0239: the maintainer or his
  delegate. #246 (agent grants) is where that would change.

### 4. Three build classes

| Class | Built from | Feed | Format read-forever |
| --- | --- | --- | --- |
| Release | `main`, through the release descriptor (ADR 0102) | nuget.org | yes |
| Chain build | a `chain/*` stable tag | a GitHub Release marked prerelease | no |
| CI artifact | any commit | Actions artifacts | no |

- **Chain builds never go to nuget.org.**
- **A chain build publishes `.nupkg` files and their SBOMs only** *(settled
  in review, 2026-11-11; open question 3)*. It publishes no GHCR image and no
  WASM bundle under a chain version. Either can be added by dated amendment
  when someone needs one.

### 5. The unstable-build marker is a reserved header byte

*(Settled in review, 2026-11-11; open question 1.)*

- **A store written by a chain build carries a non-zero reserved header
  byte**, which a chain build sets in every file header it writes. A released
  reader already refuses a non-zero reserved byte
  (`docs/spec/storage-format.md` §1: "written zero and checked zero"), so
  every release ever cut refuses the store, with no new refusal code.
- **Not a marker file.** A backup is a copy of the log, and a marker file
  beside `log/` is lost in exactly that copy. A header byte travels with the
  data.
- **A chain build writes only into a store that already carries the byte, or
  one it creates.** It never writes into a store a release wrote, because it
  cannot mark headers that already exist.
- It is a format change, so it goes through the format inventory (ADR 0216):
  an inventory entry, `docs/spec/storage-format.md`, and fixtures.

### 6. Path-encoded versions

- **A chain build's version** is `<base>.chain.<feature>.<build>[.<feature>.<build>]…`
  (SemVer 2.0 §11). For example, `0.1.0-preview.17.chain.inference.2.explorer.1`.
- **On a stable base**, the suffix attaches to the next patch:
  `1.0.1-chain.inference.1`.
- **Ordering.** A descendant extends its parent's identifiers, so it sorts
  above its parent. A chain build sorts above its base preview and below the
  next one: everything on preview.17 sorts below preview.18. Step 4 tests this
  against SemVer 2.0 §11 and against NuGet's version comparer.
- **Feature names** are restricted to `[0-9a-z-]`, with no leading zero in an
  identifier that is all digits. The gate refuses anything else.
- The git tag is `chain/<version>`, and the build reads its version from the
  tag.

### 7. Chain branches are never rewritten

- **No rebase, squash or force-push on a chain branch, and tags never move.**
- **Landing.** A child never lands before its parent; siblings land in any
  order.
- **When `main` moves**, open chain branches merge `main` forward; they never
  rebase.
- **Re-parenting** is allowed only onto a descendant of the current parent.
- **Abandonment** is a `closed/<feature>` tag; no revert is needed.
- **A chain branch never lands by squash** *(settled in review, 2026-11-11)*.
  A stable tag must stay reachable from `main` after landing, and a child is
  forked from its parent's commits. So a chain branch lands by fast-forward or
  by a merge commit. **This constrains the planned gh-tt delivery** (#243's
  probe): if `gh tt deliver` replaces `land/`, it must not squash a chain
  branch. That ADR inherits this constraint.

### 8. Dependency and interference

- **Dependency is declared at planning** (`parent`, `touches`) and made
  unavoidable by git. Undeclared use fails the build, and the fix is to
  re-parent.
- **Interference between non-ancestor pairs is found by bounded trial
  merges** *(settled in review, 2026-11-11)*:
  - every non-ancestor pair gets a `git merge-tree` check, which costs
    seconds;
  - a build and test in a scratch tree runs only for a pair whose declared
    `touches` overlap, or whose merge-tree result changes the same project;
  - trial builds run one at a time and never block a branch's own CI. The
    runner queue is already the constraint;
  - nothing from a trial is pushed.
- **Recorded interference blocks landing of the later of the two** until one
  of them merges the other's parent forward or re-parents. It is also the
  evidence that a landing train of siblings rests on (point 10).

### 9. The chain graph, checked with Varve

- **The tree is modelled as RDF.**
  - `.chain/feature.ttl` on each chain branch holds the declared facts: its
    one parent and its `touches`.
  - Facts derived from git are added on every run.
  - The graph is derived on every run and is never a source of truth.
- **The rules are a SHACL shapes graph**, validated by `Varve.Shacl`. Depth,
  diamonds and acceptance use SPARQL-based constraints (`Varve.Shacl.Sparql`).
  Each shape gets a violating and a conforming fixture graph, as an analyzer
  rule does.
- **The Varve the gate runs on is pinned** *(settled in review, 2026-11-11)*.
  - It is a released version, pinned in a lockfile and bumped like any
    dependency.
  - "The latest release" floats, and a floating gate cannot be reproduced.
    ADR 0224's locked restore applies to it like everything else.
  - It never runs on the code under test. A broken release can block chain
    work, but never a release.
- **The vocabulary namespace is `https://w3id.org/varve/chain#`** *(settled
  in review, 2026-11-11; open question 4)*. When the gate moves to
  how-we-work it takes a namespace of its own, and the move is a rename
  recorded then. The maintainer may overrule this on #230 before step 2.
- **The gate is generic.** It lives under `tools/`, beside repo-standard, and
  is written so that it can move to how-we-work with the other process gates
  (ADR 0086). Its code has no Varve-specific path or name; those are only in
  its configuration. Varve is its first consumer.
- **What the gate checks:**
  - one declared parent;
  - the fork point is a stable tag of the parent;
  - `main` is an ancestor;
  - no diamond;
  - the parent's decision set is accepted at the forked tag;
  - depth is within the backstop;
  - existing chain tags are reachable;
  - no rebase or force-push: the previous head is an ancestor of the new one;
  - the version's base, path and build number are correct;
  - the chain-build workflow refuses a commit that is not a chain stable tag,
    and the release workflow refuses a chain commit.

### 10. Landing trains

*(Settled in review, 2026-11-11. Not in #230 as written.)*

- **A train is one head that merges two or more pull request heads**, in a
  stated order, onto the current `main`.
- **Members.** Each member's own head is approved for its exact sha, and was
  green on its own.
- **Merge commits.** The train's merge commits carry `Landing-only: merge`
  and are reproducible by `git merge-tree`. A merge that needed a resolution
  by hand disqualifies the train.
- **The merge is pinned** *(settled in review, 2026-11-11)*. `eng/land.cs`,
  which builds a train, and `eng/agent-review.cs`, which judges it, run
  `git merge-tree` under the same settings, stated once in each:
  - attributes are read from the train so far (the merge's first parent),
    never from a working tree; the global and system attribute files are off;
  - rename detection is on, directory renames are off, and the rename limit
    is fixed;
  - a repository-local `info/attributes` refuses the train, because it
    changes how a merge resolves and neither tool can switch it off.

  A tree that differs anyway, for instance from a different git, **refuses
  the train**. The lander builds it again, and nothing is resolved by hand.
  A merge driver that step 6 adds (`merge=union`) comes from the tree, so it
  is pinned the same way.
- **One run.** Every gate runs once, on the train's head. Green
  fast-forwards `main` to it.
- **Releases.** A train carries at most one release descriptor, and that
  member is last. The release is cut from the train's head, as today.
- **Failure.** A red train lands nothing. `land.cs` prints the train without
  its last member, so the lander can retry a shorter one. Members stay
  individually landable.
- **Closings.** #209's check runs per member. Every member's `Completes:`
  issues need their trailers, and after the fast-forward every one of them is
  checked closed.
- **When both members are chain branches**, the train consults the trial
  merge record of point 8.
- **Who may land a train** is whoever may land today: the maintainer, or his
  delegate (ADR 0239). #246 is where that would change.

### 11. Shared allocators are designed out

- **ADR numbers are assigned at landing** *(a step of its own, settled in
  review, 2026-11-11)*.
  - On a branch, an ADR file is named by its decision set's key and carries
    no number, and text cites keys.
  - `land.cs` assigns the next free number at landing, renames the file and
    rewrites the references, in a commit marked `Landing-only: renumber` that
    it can reproduce.
  - **Until step 6 lands**, a session takes the next free number and checks
    every open pull request for a collision, as today.
- **The spec version and the changelog cut** belong to the release
  descriptor.
- **`*.Unshipped.txt` merge as `merge=union`**, through `.gitattributes`.

## Differences from #230

The review of 2026-11-11 is the later ruling. Where it changes #230's text:

| #230 as written | This ADR |
|---|---|
| "The gate runs on the latest Varve release from nuget.org" | It runs on a **pinned** release, in a lockfile, bumped like any dependency (point 9) |
| "Interference … detected by trial merges on every stable tag (build + test in a scratch tree)" | **Bounded**: `git merge-tree` for every pair; build and test only for overlapping `touches` or the same project; one at a time; never blocking (point 8) |
| Open question 1, marker byte or marker file | **A reserved header byte**, and a chain build writes only into a store it marked (point 5) |
| Open question 2, who cuts a stable tag | **A workflow, on request**, after checking every gate on that exact sha (point 3) |
| Open question 3, images and the WASM bundle | **`.nupkg` files and SBOMs only** (point 4) |
| Open question 4, the namespace | **`https://w3id.org/varve/chain#`** until the gate moves (point 9) |
| "No rebase, squash or force-push on chain branches" | Adds that a chain branch **never lands by squash**, and records it as a constraint on gh-tt's delivery (#243) (point 7) |
| The acceptance rule for stacking | Adds that **stable implies the branch's own decisions are accepted** (point 2) |
| "ADR numbers assigned at landing" | Its own **step 6**; until then, today's rule (point 11) |
| (absent) | **Landing trains** (point 10) |
| (absent) | **Chain branches are opt-in**; nothing in flight is migrated (point 1) |

## Alternatives considered

- **Status quo, one session at a time.** Rejected: independent work queues
  behind unrelated work.
- **A single linear chain.** Rejected: unrelated features would share each
  other's risk.
- **Chain builds on nuget.org as `beta`.** Rejected:
  - floating consumers would pick them up silently;
  - nuget.org has no deletion;
  - it triggers the format freeze;
  - `beta` sorts below `preview`.
- **A GitHub Packages feed.** Rejected: it needs authentication even for
  public packages, so local use needs a PAT.
- **Position numbers or session ids in versions.** Rejected: positions only
  order one line, and session ids are not readable.
- **A fixed depth of 3 as the rule.** Rejected as the rule, because it is
  arbitrary: the real cost is review pressure on unaccepted ancestors, which
  the acceptance rule addresses. 3 stays as the backstop.
- **Chain state in a script.** Rejected: it loses the declarative rules, the
  queryable history and the dogfooding.
- **A marker file beside `log/`** (open question 1). Rejected: a backup
  copies the log and loses the file.
- **Stable tags cut by hand, or on every green head** (open question 2).
  Rejected: by hand adds nothing to "every gate is green", and every green
  head makes tags nobody asked for.
- **The gate on the latest release.** Rejected: a floating gate cannot be
  reproduced.
- **A build and test for every non-ancestor pair.** Rejected: the runner
  queue is already the constraint. `git merge-tree` finds textual
  interference for every pair at no cost, and a build runs only where the
  declared or merged footprint overlaps.
- **GitHub's merge queue, in place of trains.** Not chosen. A merge queue
  lands through GitHub's merge, on branches it names itself. This repository
  lands a checked head by fast-forward through `land/` (ADR 0088 point 2),
  cuts a release only from a head landed that way (ADR 0102), and judges
  agent commits on that push (ADR 0087). A train keeps `land.cs` and the
  `land/` route, and adds only a head built from approved heads.

## Consequences

- **Landing costs one run per train, not per pull request.** A train that
  goes red costs one more run per shorter retry, and its members stay
  landable one at a time.
- **`agent review` judges a train.** On a `land/` push whose head is not an
  approved pull request's head, it admits a head whose first-parent walk to
  `main` is only `Landing-only: merge` commits, each one reproducible by
  `git merge-tree` and each one's second parent an approved pull request's
  head. The approvals are still the members' own; nothing on the `land/`
  branch can add one (step 1).
- **Independent features stop waiting for each other**, and any stable
  feature can be consumed from the repository.
- **Squash merges are gone for chain branches**, and forward merges add
  merge commits.
- **ADR numbers on chain branches are provisional** until step 6; text cites
  keys.
- **The gate depends on a pinned Varve release.** A broken release can block
  chain work, but never a release.
- **Every artefact these steps change:**
  - dated notes on ADR 0088 and ADR 0102 (this pull request);
  - `AGENTS.md`'s landing section and `docs/releases.md` (step 1);
  - the format inventory and `docs/spec/storage-format.md` (step 3);
  - `.github/workflows/release.yml`, and a tagging workflow and a chain-build
    workflow (step 4);
  - the contributing guide, the session prompt template, `.gitattributes`,
    and the issue template's `parent` and `touches` (step 6).

## Not in this work

- Migrating open pull requests to chain branches.
- Replacing `land/` with gh-tt. That is its own ADR, and it inherits the
  no-squash constraint (point 7).
- Agent grants (#246). Where a step needs "who may tag" or "who may land a
  train", it uses today's rule (ADR 0239) and says where #246 would change it.

## Checks

- **Checked against the accepted ADRs** in `docs/adr/`:
  - **0088**: `main` moves only to a checked commit. A train's head is
    checked like any other, and a merge commit made on a branch is a commit
    like any other. Amended to say both;
  - **0102**: a release is cut from a commit landed through `land/`, and a
    train's head is one. Amended to put chain builds outside a release's
    scope;
  - **0072** and **0216**: a reserved byte is checked zero, and a format
    change goes through the inventory (step 3);
  - **0087**: an approval is of a pull request's head, and a train's members
    are each approved;
  - **0224**: the gate's Varve is restored locked;
  - **0239**: who may tag and land today.

  0085 is superseded by 0102. No conflict.
- **Supersedes** no ADR.
- **The brief.** It does not depart from `docs/brief.md`: the brief governs
  the product, and this ADR governs how changes reach it.
- **Dependencies.** None new. The gate (step 2) consumes Varve's own released
  packages, pinned under ADR 0009's register like any dependency.
- **Revisit condition.** This ADR is accepted ahead of the evidence that
  trains land faster than serial landing. Revisit it if, over the first ten
  trains, more than half go red and land shorter, or the median train runs
  longer than two serial landings.
- **AOT, trimming or WASM.** Not affected.

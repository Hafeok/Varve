# `model-names` — a commit message never names a model

**Decision:** ADR [0241](../adr/0241-model-names-refused-identifiers-allowed.md),
`ModelNamedInTheRecordNotTheCommit`, which supersedes that ruling of ADR
[0033](../adr/0033-commit-traceability.md)'s amendment of 2026-10-02. Built for
[#169](https://github.com/Hafeok/Varve/issues/169).

## What it reports

A line of a commit message that names a model: a family word listed in
`eng/model-families.txt`, optionally after `Claude`, followed by a version.
Case does not matter. A finding names the commit and the line.

Allowed:
- a model's identifier in the API's hyphenated form, `claude-<family>-<n>-<n>`;
- the bare product name, as in `Co-Authored-By: Claude <noreply@anthropic.com>`;
- a family word with no version, and a version with no family word.

## Where it is held

Three places, earliest first. The pattern lives once, in
`eng/model-names.cs` and `eng/model-families.txt`, and the hook and the gate
both run that script.

1. **A tool's default.** `.claude/settings.json` sets one tool's default
   co-author trailer to the allowed one. Other tools have their own settings;
   `AGENTS.md` states the rule, not a tool's configuration (ADR 0033).
2. **The commit.** `eng/hooks/commit-msg` (`git config core.hooksPath
   eng/hooks`) rewrites a `Co-Authored-By:` trailer that names a model to the
   allowed one, folding a repeat, and refuses a name anywhere else with the
   line quoted, for every author and every tool. When the SDK is missing it
   says so and leaves the message to the gate.
3. **The gate.** `dotnet run eng/model-names.cs` checks every commit a pull
   request or a `land/` push adds over `main`, merges included, worked out from
   the environment as `eng/issue-refs.cs` does. Commits reachable from
   `e684009a` are left out: ADR 0241 point 4 leaves history as it is. The
   bound was `2cad18d5`, the last such commit on `main` on 2026-11-11, and moved
   to `e684009a`, `main`'s head of 2026-11-12, when #236 landed two older
   commits whose co-author trailers name a model (`2d94eb94`, `195a8cdf`). `ci.yml`'s release call skips it, as
   it skips the other range gates.

## How to satisfy it

Reword the line, or write the identifier instead of the name. A commit already
made is reworded (`git commit --amend`, or a rebase on a branch of your own)
and pushed again; with the hook set, the trailer is fixed as it is made.

## Its failure path

`tests/fixtures/model-names/`, run by the `model-names-fixtures` job: a name
alone, after `Claude`, in the subject, in the body, in a trailer the hook does
not rewrite and in lower case, each refused; an identifier, the allowed
trailer, a family word without a version and a version without a family word,
each accepted; and three rewrites by the hook. The versions in the fixtures are
fictional.

## Exception path

The rule has no suppression; a line that must quote a name is reworded.

One list is skipped: `eng/model-names-grandfathered.txt`, exact commit hashes,
each with its pull request and the reason (the maintainer's ruling of
2026-11-16). It holds commits that were pushed before the gate reached `main`
and name a model in a co-author trailer. A pushed branch is never
force-pushed, so they cannot be reworded, and without the list their pull
requests could not land once the gate is on `main`. The lower bound stays at
`e684009a`.

- **Nothing pushed after the gate is on `main` may be added without the
  maintainer's approval.** A commit made after that is reworded before it is
  pushed; the hook does it as the commit is made.
- The list was drawn up by running the gate over every open pull request's
  head on 2026-11-16: seven commits of #268 and four of #264, and no other.
- An entry is the full hash, `#N` and the reason. A line in any other shape
  stops the gate with exit code 2.

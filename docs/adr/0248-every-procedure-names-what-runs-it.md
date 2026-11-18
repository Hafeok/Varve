# 0248 — Every procedure in the operator's guide names what runs it

## Status

**Proposed — filed unaccepted by #129, 2026-11-18** (ADR 0066), under #129's
operator-guide deliverable. It records the maintainer's answer 5 of
2026-11-16 on #129: yes to the markers and their gate. Acceptance is the
maintainer's act on the pull request.

Records decision set
[`every-procedure-names-what-runs-it`](../decisions/every-procedure-names-what-runs-it.md).

## Context

#129 asks of the operator's guide: "Every procedure in it runs in CI, or the
guide says why it can't." The audit on #129 (2026-11-16) found the opposite
state: no page said which of its procedures ran anywhere, and 34 procedures
ran in no job. Some of them were wrong on `main`, and nothing ran them to
notice. Examples: a JSON configuration outside the `Varve` section, a route
that does not exist, and an image tag that is never published.

A rule only in a document is not a rule. The guide needs a check that fails
when a procedure says nothing about what runs it, and when what it names is
gone.

## Decision

1. **A procedure is a fenced code block** in `docs/operator/*.md` and
   `docs/auth-providers.md`: a command, a configuration to apply, a
   declaration, a request.
2. **The line before it** (blank lines aside) is its marker, an HTML comment
   that the rendered page doesn't show. It takes one of three forms:
   - `<!-- ci: <workflow file> <job id>; eng/ci.cs <job> — <note> -->` names
     one or more jobs that run it. Each is a job id in a workflow under
     `.github/workflows/`, or a job of `eng/ci.cs`'s list. The note, after
     " — ", says which test runs it, and how it differs from the page when
     it runs an equivalent rather than the block itself.
   - `<!-- ci: none — <reason> -->` says why no job runs it.
   - `<!-- ci: example — <what it shows> -->` marks a block that is no
     procedure: a command's output, a response, a rendered event.
3. **A page with a `none`** has a section headed "Not run in CI" listing
   each one with its reason.
4. **The gate is `eng/procedures.cs`,** the job `procedures` in `eng/ci.cs`.
   It fails on:
   - a block with no marker;
   - a marker naming a workflow or a job that doesn't exist;
   - a `none` or an `example` with no words after it;
   - a page with a `none` and no "Not run in CI" section.

   With `--report`, it prints every procedure that no job runs, with its
   reason. Its failure path is proven on `tests/fixtures/procedures/`.
5. **A page whose markers belong to another open pull request** is exempt by
   name in `eng/procedures-exemptions.txt`, with that pull request. An
   exemption for a page with no unmarked block fails, and so does one for a
   page the guide doesn't have. The first exemption is `web-ui.md`, which
   #308 rewrites.
6. **What the gate doesn't judge:** that the named job runs exactly the
   block. That is review's, against the note. A step list written as prose
   is marked by hand in its page's "Not run in CI" section where nothing
   runs it.

## Alternatives considered

- **Run every block in CI by extracting it.** Rejected: many blocks need a
  tenant, a subscription, a systemd host or a person's sign-in, so the
  extractor would need its own list of what to skip, which is this marker
  under another name.
- **A table of procedures per page.** Rejected: it drifts from the blocks it
  lists. A marker sits beside its block, and moves and is deleted with it.
- **Markers only on what nothing runs.** Rejected: a block with no marker
  would then claim to be run, by default, with nothing checking which job.

## Consequences

- A renamed or removed job fails the gate on every page that names it, so a
  procedure can't silently stop being run.
- `--report` is the list #129's close-out needs: every procedure that still
  runs in no CI job, and why.
- Writing a page costs one comment line per block.

## Checks

- `procedures` and `procedures-fixtures` in `eng/ci.cs`.

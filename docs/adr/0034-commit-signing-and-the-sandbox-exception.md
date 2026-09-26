# 0034 — Commit signing and the sandbox exception

## Status

Accepted. 2026-09-22. Carries a **revisit condition**; see the end.
**Amended by [0066](0066-expected-red-pull-requests.md)** (2026-09-25): the
maintainer's acceptance of a session's filed decisions is a signed human
commit on that session's branch, and a session never writes `accepted-by`.

## Context

The Mind Over Machine stewardship standard requires signed commits:

> **Cryptographic Security:** Built-in support and requirement for GPG-signed
> commits.

Varve's history is unsigned in GitHub's eyes. Ruleset 2 on `main` now requires
signed commits, with the GitHub App that pushes AI-session commits on its bypass
list. This ADR records what that arrangement actually is, because the obvious
description of it is wrong.

### What was measured, not assumed

The expected story was that a cloud sandbox cannot hold a signing key, so its
commits cannot be signed. **That is false in this environment, and it was
checked rather than assumed.**

The sandbox holds an ed25519 SSH key and signs every commit it makes. The
merged milestone 3b commits carry SSH signatures; so does a probe commit made
for this ADR. What fails is on GitHub's side:

```
GET /repos/Hafeok/Varve/commits/7e4589e  →  commit.verification
  verified : false
  reason   : "unknown_key"
  signature: -----BEGIN SSH SIGNATURE----- …
```

The signature is real and well-formed. GitHub reports `unknown_key` because that
public key is not registered as a signing key on any account it knows. So the
problem is **registration, not capability**, and the fix that suggests itself —
register the key — is the one that must not be taken. See the alternatives.

### Two experiments, run and reported without acting on them

**1. GraphQL `createCommitOnBranch`.** GitHub signs commits created through this
mutation with its own key, which would make sandbox commits verified with no
bypass at all. **It cannot be attempted from here**: the session's egress proxy
refuses every GraphQL request, mutation and query alike, with a message
directing callers to REST. This is a limit of the sandbox, not of GitHub, and it
could lift without notice.

**2. The REST contents API.** `PUT /repos/{owner}/{repo}/contents/{path}` is the
same idea by a route that *is* open, and the hypothesis was that it too would be
signed by GitHub. **It is not.** A probe commit made through it came back:

```
verified : false
reason   : "unsigned"
signature: NONE
```

GitHub's automatic signing applies to commits made through the web interface,
not to every commit made through the API by a token. So neither API route yields
a signed commit from this sandbox today, and the hypothesis that the second one
would is recorded here because it is the obvious guess and someone will make it
again.

### Why the signature and the sign-off are different things

They are routinely conflated and they answer different questions.

- A **signature** is cryptographic evidence of *who made this commit*. It
  resists a forged author line, which git does not otherwise check at all.
- A **DCO sign-off** (`Signed-off-by:`) is a statement that *the signer has the
  right to contribute this code* under the project's licence. It is an
  assertion, not a proof, and no cryptography makes it true.

Under a copyleft licence (ADR 0031) the second matters more than it did under
Apache-2.0, because the terms a contribution arrives under determine the terms
it can be redistributed under. Neither substitutes for the other.

## Decision

1. **Signed commits are required on `main` for every human committer**, from
   now on. GPG or SSH signing, either is acceptable — GitHub verifies both, and
   the standard's word "GPG" is about the property, not the algorithm.
2. **Existing history stays as it is.** Rewriting it to add signatures would
   change every commit hash in the repository, break every `Claude-Session`
   trailer's relationship to its diff, invalidate the three merged pull
   requests, and prove nothing about commits made before the rule existed.
3. **DCO sign-off is required from every commit**, human and AI session alike,
   with no exception. `Signed-off-by:` names a person who may contribute the
   code. It is required from an AI session precisely *because* a session cannot
   assert anything: the sign-off is the directing human's statement, and a
   session that cannot produce one has nobody standing behind its output.
4. **Commits from AI sessions in the cloud sandbox are exempt from the signature
   requirement**, through the bypass granted to the GitHub App that pushes them
   in ruleset 2. **This is a stated deviation from the standard**, not an
   interpretation of it.
5. **What attests those commits instead** is two things that do not depend on a
   key we control:
   - **The push path.** Only the maintainer's installation of the App can push
     to this repository. A commit on `main` from a sandbox is one the
     maintainer's own credentials admitted.
   - **The traceability record** (ADR 0033): the session's prompt, tool, model
     and report, tied to an issue. That is a stronger statement about a commit's
     provenance than a signature is — a signature says a key was present, and
     the record says what was asked for and what came back.
6. **A local session with a key signs like a human.** The exception is scoped to
   the cloud sandbox, which is the only place the key problem exists. A session
   running on the maintainer's machine uses the maintainer's key and gets no
   bypass.

### Amendment, 2026-09-23 — a cloud session bound to a branch lands through a pull request

Point 4 above, and the rejection below of "forbid AI sessions from pushing to
`main`", assumed every cloud session could push to the trunk. **Not every one
can.** A cloud session may be configured with a designated development branch,
and such a session can push only to that branch: the platform refuses a push to
any other, `main` included. The milestone 4 session (issue #8) was configured
that way.

Such a session **lands its work through a pull request from its branch**, which
the maintainer merges — with the admin override where the ruleset's required
review would otherwise hold it, since the blocking review here is the
automated one (ADR 0032). The route is still a choice in the sense ADR 0032
means: it is the session's configuration that makes it, not a rule that treats
AI commits differently. Nothing else in this ADR changes. The commits are
signed with the sandbox key, are unverified for the reason recorded above, carry
the DCO sign-off, and have a traceability record.

## Alternatives considered

- **Register the sandbox's SSH public key as a signing key on the maintainer's
  GitHub account.** This is the smallest possible change — it makes every
  existing sandbox commit verified retroactively, needs no bypass, and removes
  the deviation from the standard entirely. **Rejected, and it is the important
  rejection in this ADR.** The key is provided by the sandbox platform, not
  generated by or for this project; the maintainer does not control it, cannot
  rotate it, and has no way to know its blast radius. On the evidence it is
  reused across sessions — the probe commit and the milestone 3b commits carry
  the same key — and plausibly across every cloud session belonging to every
  user of that platform. Registering it would mean any such session could
  produce commits that GitHub attests as the maintainer's, anywhere, forever,
  until the registration is removed. A verified badge that means "some machine
  somewhere held the shared key" is worse than an honest absence of one, because
  it is trusted more.
- **Re-sign every agent commit by hand before it reaches the trunk.** The
  maintainer fetches the branch, re-commits with his own key, pushes. Rejected:
  it makes trunk-based development (ADR 0032) hold for humans only. Every AI
  session would end in a queue waiting for a person, which is precisely the
  blocking-review arrangement that decision removed, reintroduced under another
  name and applied selectively. It would also make the maintainer the author of
  code he did not write, which is worse for traceability than the exception is.
- **Forbid AI sessions from pushing to `main` at all**, requiring a pull request
  for every one. Rejected for the same reason, and it contradicts ADR 0032
  directly: the route is a choice, and making it a choice only for humans is not
  a choice.
- **Drop the signing requirement entirely**, since the App bypass means `main`
  will carry unsigned commits regardless. Rejected: it would give up the
  protection for the case where it works. The maintainer's own commits, and any
  future contributor's, are signed and verified, and the set of commits that are
  not is exactly the set with a traceability record explaining them.
- **Wait for the sandbox to support signing properly.** Rejected as a decision
  that is really a deferral: work is happening now, commits are landing now, and
  "we will decide when the platform changes" is how a repository ends up with no
  rule at all.

## Consequences

- **`main` will carry unsigned commits, visibly, and that is the deal.** Anyone
  reading the history sees unverified commits from the App and verified ones
  from the maintainer. The unverified ones each have an issue and a
  traceability record; that is where their provenance lives.
- **The deviation is written down in three places** — here, in `GOVERNANCE.md`
  and in `AGENTS.md` — so that a reader of any of them finds it rather than
  discovering it from a badge.
- **`CONTRIBUTING.md` carries the exact `git config` for both mechanisms**, for
  GPG and for SSH signing, and for the sign-off. A requirement whose setup is
  not written down is a requirement people work around.
- **Ruleset 2 is the enforcement and it is a GitHub setting**, not a file in
  this repository. Nothing here can check it. If the bypass list is edited, this
  ADR silently stops describing reality — which is an argument for reading it
  whenever the rulesets are changed, and is why `GOVERNANCE.md` documents them.
- **The probe branch `experiment/api-signed-commit` could not be deleted from
  the sandbox**: the push proxy refuses ref deletions and the REST delete
  returns 403. The maintainer deletes it; it holds one file and one commit and
  affects nothing.

## Revisit condition

**If a route appears by which a sandbox commit is signed by a key the project
controls, or by GitHub itself, this exception is superseded.** Two are already
identified and neither is available today: the GraphQL `createCommitOnBranch`
mutation, which is blocked by the session's proxy rather than by GitHub, and any
future support for a per-installation or per-session signing key. The REST
contents API has been tested and is *not* such a route.

When one becomes available, the successor ADR removes the bypass from ruleset 2
rather than amending this one.

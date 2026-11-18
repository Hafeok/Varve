# `eng/land.cs`'s closing-trailer fixtures

Cases for `eng/land.cs --fixtures` (#209). Each holds a `step`, the pushed
`commits` (`sha`, `message`) and either the pull request's `body` (`push`, the
`Completes:` check before a push) or the issues' `states` (`landed`, the check
after `--main` has moved `main`; `null` when GitHub cannot be read). `says`
lists text the output must contain. A `date` case holds a descriptor's
`version` and `date` and the `landing` day (#248). A `train` case (#277) holds
`main`'s files and the `members`, each a `number`, the `files` its one commit
adds over main, its `approval` (`head`, `other` for one naming another sha, or
`none`), whether it is `green`, and optionally its `body` and commit
`message`; `edit` replaces the last merge with one whose tree was changed by
hand; `infoAttributes` writes the scratch repository's `info/attributes`. An `approval` case (#290) holds a `head` and one `comment` (`body`, `created_at`, `updated_at`), and passes when `eng/lib/Approvals.cs`, the reader `eng/land.cs` and `eng/agent-review.cs` share, takes the comment as an approval of that head; in the body `{line}` stands for the approval line for the head, `{line12}` for the same with 12 characters of it and `{lineupper}` for the same in capitals, so that no file here holds the line itself. A train case is built for real, in a scratch bare repository: checked,
merged by `git merge-tree`, built twice to show it is the same, and verified as
`--main` and `agent review` verify it. The pipeline's `land-fixtures` job runs
them on every build: a `pass-` case must pass and a `fail-` case be refused.

| Case | Step | Why |
|---|---|---|
| `pass-completes-with-trailer` | push | `Completes: #1`, and a commit closes #1 |
| `fail-completes-without-trailer` | push | `Completes: #1`, and no commit closes it; refused naming #1 |
| `pass-completes-none` | push | `Completes: none`, no closing trailer |
| `fail-no-completes-line` | push | no `Completes:` line; one inside an HTML comment does not count |
| `fail-completes-malformed` | push | the template's placeholder left as it is |
| `pass-descriptor-trailer-counts` | push | a release descriptor's own `Closes #14` counts |
| `pass-landed-all-closed` | landed | every issue a trailer names is closed |
| `fail-landed-issue-still-open` | landed | #2 is still open; reported with its commit |
| `fail-landed-state-unreadable` | landed | GitHub cannot be read; non-zero, and it says so |
| `fail-date-four-days-before-landing` | date | dated four days before the landing day; refused, printing the `--sbom` command |
| `pass-date-three-days-before-landing` | date | three days is inside the window |
| `fail-date-missing` | date | no `date:` to judge |
| `pass-train-two-members` | train | two approved, green members; two `Landing-only: merge` commits, the same head when built twice, `land/train-11-12` |
| `pass-train-descriptor-last` | train | the release descriptor's member last; `land/release-<version>` |
| `fail-train-one-member` | train | a train has two or more members; says to land it alone |
| `fail-train-approval-for-another-sha` | train | #12's approval names another sha |
| `fail-train-member-not-green` | train | a member with a failed required check |
| `fail-train-member-completes-unmet` | train | a member's `Completes: #5` with no trailer in its own commits |
| `fail-train-two-descriptors` | train | two members add release descriptors |
| `fail-train-descriptor-not-last` | train | the descriptor's member is not last |
| `fail-train-merge-conflicts` | train | #13 conflicts with #12 in `README.md`; prints the train without #13 |
| `fail-train-merge-edited-by-hand` | train | the last merge's tree is not `git merge-tree` of its parents |
| `pass-train-attributes-from-the-train` | train | `.gitattributes` in main's tree sets `merge=union`; the merge reads attributes from the train, never a working tree, so it is clean (ADR 0243 point 10) |
| `fail-train-info-attributes` | train | a repository-local `info/attributes`; refused, because no option switches it off |
| `fail-train-shorter-is-a-plain-landing` | train | #12 conflicts with #11; the shorter form has one member, so it prints `dotnet run eng/land.cs -- 11` |
| `pass-approval-whole-comment` | approval | the comment is the approval line and nothing else |
| `pass-approval-delegate-suffix` | approval | the same, ending ` (delegate)` (ADR 0239) |
| `pass-approval-surrounding-whitespace` | approval | newlines around the line are trimmed |
| `fail-approval-inside-a-sentence` | approval | the line quoted in a status report, which is what #290 found |
| `fail-approval-in-a-code-span` | approval | the line between backticks |
| `fail-approval-after-another-line` | approval | "lgtm", then the line |
| `fail-approval-twelve-characters` | approval | a prefix of the head names no head |
| `fail-approval-uppercase-sha` | approval | the sha in capitals |
| `fail-approval-other-suffix` | approval | a suffix that is not the delegate's |
| `fail-approval-edited-comment` | approval | `updated_at` is later than `created_at` |
| `fail-approval-no-timestamps` | approval | a comment that cannot be shown unedited |

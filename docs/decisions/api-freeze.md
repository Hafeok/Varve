---
set: api-freeze
namespace: varve
adr: 0227
decisions:
  - key: ThePublishedPackageIsTheBaseline
    statement: "Before 1.0 each library package is compared with its own nupkg published at the newest release tag that is an ancestor of the commit, by the SDK's package validation; a tool package is reported as not a library and a package with no published baseline as new"
  - key: OnePackageAtATime
    statement: "eng/api-freeze.cs packs one package at a time, Directory.Build.props turning validation on only for the project VarveApiFreezeProject names, and reads the findings validation writes as a suppression file; no suppression file is committed"
  - key: AnUnmarkedBreakFailsBeforeOnePointZero
    statement: "Before 1.0 every finding is reported, and one fails only when no commit since the baseline tag that touches the package is marked `!` or `BREAKING CHANGE:` and no proposed descriptor's summary names the package in a paragraph or item saying (breaking); a marking commit covers every finding in its package over the range"
  - key: ABreakNeedsANewMajorFromOnePointZero
    statement: "From 1.0 every finding fails unless a proposed descriptor's major is above the baseline's, marked or not"
  - key: RequiredAndRunAtTheRelease
    statement: "ci.yml's api freeze job runs on every pull request, push and release call and is a required check; eng/ci.cs runs the rule's fixtures with no network"
  - key: TestOnlyMembersAreReported
    statement: "eng/api-freeze.cs --test-only lists every visible type and member of a library package that a test assembly references and no other assembly does, counting overrides, interface members, constants and unreferenced members apart; it reports for the maintainer's decision and never fails"
---

The rulings of [ADR 0227](../adr/0227-the-public-api-freeze-gate.md), filed
unaccepted by readiness B for #129 (ADR 0066).

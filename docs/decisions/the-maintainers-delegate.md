---
set: the-maintainers-delegate
namespace: varve
adr: 0239
decisions:
  - key: OneDelegateOnTheMaintainersMachine
    statement: "The maintainer's delegate is one Claude Code session on the maintainer's own machine, signing with his YubiKey; a cloud session, another machine or another key is not one"
  - key: TheDelegateApprovesLandsAndAccepts
    statement: "The delegate may approve a pull request's head, land it, and write accepted-by naming the maintainer for a decision the maintainer has read; it decides nothing for him"
  - key: DelegateApprovalsAreMarked
    statement: "A delegate's approval reads approve <sha> (delegate)"
  - key: DelegatedCommitsCarryTheTrailer
    statement: "A commit the delegate makes for the maintainer, to accept a decision or to land a pull request, carries the trailer Delegated-by: Emil Okkels Klein <emil@okkels-klein.dk>"
  - key: TheDelegatesOwnWorkIsTheAgents
    statement: "A commit containing the delegate's own implementation work is authored as Claude (Emil), the agent of the identity map, and signed off by the maintainer"
---

The rulings of [ADR 0239](../adr/0239-the-maintainers-delegate.md),
filed unaccepted by the landing session for #244 (ADR 0066).

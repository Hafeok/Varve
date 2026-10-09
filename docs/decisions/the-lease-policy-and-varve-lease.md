---
set: the-lease-policy-and-varve-lease
namespace: varve
adr: 0116
decisions:
  - key: RefusedLeaseIsWaitedForNotTaken
    statement: "At start a refused lease is retried every second for Varve:Lease:WaitFor, logging the holder from LOCK.owner at each retry, and then the dataset is failed with the holder as its reason"
  - key: NothingForcesAHeldLock
    statement: "No interval, command or setting ever breaks a lock the operating system still holds, because a held lock has a live holder and deleting it would let a second process in"
  - key: VarveLeaseInspectsAndBreaksOnlyWhenFree
    statement: "varve lease <dir> reports the owner and whether the lock is held; varve lease --break <dir> takes the lock to prove it free, then removes the owner file and reports whose it was, and refuses naming the holder when it is held"
---

The rulings of [ADR 0116](../adr/0116-the-lease-policy-and-varve-lease.md), filed unaccepted by milestone Operability of #12
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

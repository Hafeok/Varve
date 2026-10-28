# CommitList

The commits resource as a list, newest first, with the live tail of the feed at its head.

- **Consumer provides:** each commit's position (linked to its delta), time, agent, cause (the request id that made it) and the counts of assertions and retractions; whether the tail is live or paused.
- A commit arriving through the live tail renders with `vw-commit--new` for a few seconds, then settles. The tail indicator pauses when the operator scrolls away from the top, and says so.
- Commits outside the caller's graph scope are not shown; the list never shows a gap as an error.

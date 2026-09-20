# Conformance baseline

`passing.txt` holds the test IRIs that pass, one per line, sorted. It is empty:
no parser exists, so nothing passes, and that is the correct state for
milestone 1.

`eng/ratchet.cs` reads the TRX from a conformance run and compares it against
this file. It fails when a listed test stops passing, and when a listed test is
not in the run at all — the second check is what turns a renamed or silently
dropped case into a build failure rather than a quiet loss of coverage. Newly
passing tests are printed, not failed: a ratchet that failed on improvement is
a ratchet people route around.

When a change makes tests pass, add them here in the same pull request. The
ratchet prints the exact lines to add.

See `docs/adr/0007-w3c-conformance-harness.md`.

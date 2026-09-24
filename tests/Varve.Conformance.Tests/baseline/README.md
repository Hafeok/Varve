# Conformance baseline

`passing.txt` holds the test IRIs that pass, one per line, sorted. At
milestone 5a it holds all 883 cases of the wired RDF suites and all 554 cases
of the SPARQL 1.0, 1.1 and 1.2 syntax suites (`SparqlSuite.All`), 1,437 in
all.

`exemptions.txt` holds cases we have decided not to pass yet, as
`<test IRI> <justification>`. An exempt case is neither required to pass nor
reported as newly passing, an exemption with no justification fails the
ratchet, and an exemption for a case that now passes is reported so it can be
removed. It is empty, which is a result rather than a default: no RDF case
and no SPARQL syntax case needs one, not even the SPARQL 1.0 negatives, none
of which SPARQL 1.1 relaxed.

`eng/ratchet.cs` reads the TRX from a conformance run and compares it against
this file. It fails when a listed test stops passing, and when a listed test is
not in the run at all — the second check is what turns a renamed or silently
dropped case into a build failure rather than a quiet loss of coverage. Newly
passing tests are printed, not failed: a ratchet that failed on improvement is
a ratchet people route around.

When a change makes tests pass, add them here in the same pull request. The
ratchet prints the exact lines to add.

See `docs/adr/0007-w3c-conformance-harness.md`.

---
set: the-validation-report-on-the-commit
namespace: varve
adr: 0173
decisions:
  - key: AffectedReportAttachment
    statement: "An accepted commit under a report or reject binding carries one validator attachment per binding, the triple term (binding, varve:affectedReport, sha256 hex literal) over the canonical N-Quads of the report restricted to the commit's affected pairs"
  - key: ReportStoredOnce
    statement: "The report is stored once, in the projection; the attachment is a reference and a digest of the slice the commit decided on"
  - key: DigestMismatchIsAnEngineChange
    statement: "A digest that differs after an upgrade is an engine change, and a release that changes SHACL results says so in its notes"
---

The rulings of [ADR 0173](../adr/0173-the-validation-report-on-the-commit.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 2a (#138, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.

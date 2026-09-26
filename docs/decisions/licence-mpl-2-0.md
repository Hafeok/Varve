---
set: licence-mpl-2-0
namespace: varve
adr: 0031
decisions:
  - key: ProjectLicence
    statement: "Varve is licensed under MPL-2.0, file-level copyleft, with the canonical text verbatim in LICENSE"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: ExhibitANoticeOnEveryFile
    statement: "Every .cs file carries the MPL-2.0 Exhibit A notice as its first three lines, enforced by eng/licence-headers.cs with generated code excepted by pattern"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: PackageLicenceExpression
    statement: "PackageLicenseExpression is the SPDX expression MPL-2.0, and eng/package-metadata.cs reads it back out of every built package"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: LicenceAndNoticePacked
    statement: "LICENSE and NOTICE are packed into every package, and the metadata gate requires both to be present in the archive"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: NoticeNamesCopyrightHolder
    statement: "NOTICE names the copyright holder, Emil Okkels Klein, and says in plain language what file-level copyleft asks of a consumer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
  - key: NoCodeCopiedWhateverTheLicence
    statement: "No code is copied from Oxigraph or dotNetRDF, whatever their licences permit"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-22T00:00:00Z
---

The rulings of [ADR 0031](../adr/0031-licence-mpl-2-0.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Carries the rulings of ADR 0002, which 0031 superseded whole, and 0029's licence expression.
`NoticeNamesCopyrightHolder` is 0002's closed open question (the holder, named in NOTICE) as
0031 keeps it; 0002's per-file-header answer is reversed by `ExhibitANoticeOnEveryFile`.

# RealEstateCore 4, vendored

The reference domain for learning and testing (ADR 0141): RealEstateCore's
SHACL release at the pinned tag **`v4.1.0`** (29 June 2026), copied from
<https://github.com/RealEstateCore/rec/tree/v4.1.0/Source/SHACL>. Test and
sample data only: no package references it and no shipped assembly contains
it (ADR 0141, decision 1). It is never fetched at test time.

| File | From | What |
|---|---|---|
| `v4.1.0/rec.ttl` | `Source/SHACL/RealEstateCore/rec.ttl` | the REC 4 shapes |
| `v4.1.0/recimports.ttl` | `Source/SHACL/RealEstateCore/recimports.ttl` | the terms REC's shapes name from QUDT, GeoJSON and others |
| `v4.1.0/Brick+patches.ttl` | `Source/SHACL/Brick/Brick+patches.ttl` | Brick, as REC patches it, for the point and equipment classes |
| `v4.1.0/REC.LICENSE` | `LICENSE` at the tag | BSD-3-Clause, RealEstateCore Consortium |
| `v4.1.0/Brick.LICENSE` | `LICENSE` of BrickSchema/Brick | BSD-3-Clause, Brick Consortium |

`v4.1.0/SHA256SUMS` holds each file's SHA-256; the generator's tests check
them before reading a byte. Moving to another tag is a pull request that adds
its directory, reruns every REC-based gate, and removes the old one (ADR
0140). `THIRD-PARTY-NOTICES` at the repository root carries both notices.

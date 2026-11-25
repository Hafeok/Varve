# The REC terms, by the chapter that introduces them

The learning track uses one domain, a RealEstateCore portfolio
([ADR 0141](../adr/0141-realestatecore-as-the-reference-domain.md)), and
introduces its vocabulary a little at a time. A chapter uses only the `rec:`
and `brick:` terms of its own row and the rows before it. `eng/learn.cs`
holds every page and every sample to this table: a term used before its
chapter, or in no row, fails the `learn` job
([ADR 0249](../adr/0249-the-learning-tracks-pages-quote-what-ci-ran.md)).

`rdf:`, `rdfs:` and `xsd:` are RDF's own vocabularies, not REC's, and appear
from chapter 1 without a row.

| Chapter | Terms |
|---:|---|
| 1 | `rec:Room`, `rec:area`, `rec:ArchitectureArea`, `rec:netArea`, `rec:capacity`, `rec:ArchitectureCapacity`, `rec:seatingCapacity` |
| 2 | `rec:Site`, `rec:Building`, `rec:Level`, `rec:levelNumber`, `rec:hasPart`, `rec:isPartOf`, `rec:Asset`, `rec:locatedIn`, `rec:isLocationOf`, `rec:hasPoint`, `brick:Temperature_Sensor`, `brick:CO2_Sensor` |

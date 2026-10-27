# The REC gate's features report

REC 4.1.0's shapes in the gate's composition (ADR 0176): `rec.ttl`, `Brick+patches.ttl`,
`recimports.ttl` and `testdata/rec/gate/composition.ttl`, read under
`varve:subClassOfInShapesGraph` with SHACL-SPARQL. Written by
`RecGateTests.The_features_report_is_the_pinned_one` and pinned by it.

## Shapes by class (ADR 0171)

| Class | Shapes |
|---|---:|
| Incremental | 349 |
| Scoped | 1961 |
| Full | 0 |

## What puts a shape in its class

| Construct | Section | Class | Shapes, by their own construct | Shapes, by a shape they refer to |
|---|---|---|---:|---:|
| a SPARQL-based constraint, by its join paths to $this | SHACL 1.2 SPARQL-Related Features §3–4 | Scoped | 3 | 0 |
| sh:class on rdfs:subClassOf in the data graph | SHACL 1.2 Core §7.1.1 | Scoped | 203 | 139 |
| sh:targetClass on rdfs:subClassOf in the data graph | SHACL 1.2 Core §3.1.3.2 | Scoped | 1708 | 0 |

## The SHACL terms used

| Term | Uses | Role | Class |
|---|---:|---|---|
| `sh:NodeShape` | 1728 | node shape (Core §3.2), with implicit class targets when also an `rdfs:Class` (§3.1.3.3) | incremental on `rdf:type`; scoped on a subclass change |
| `sh:PropertyShape` | 442 | property shape (Core §3.3) | the class of its path and constraints |
| `sh:SPARQLConstraint` | 3 | SPARQL-based constraint (SHACL-SPARQL §3) | scoped when analysable (ADR 0171) |
| `sh:SPARQLRule` | 78 | SHACL-AF rule | not evaluated (ADR 0149) |
| `sh:TripleRule` | 5947 | SHACL-AF rule | not evaluated (ADR 0149) |
| `sh:and` | 4 | logical (Core §7.7.2) | the class of the shapes referenced |
| `sh:class` | 203 | value type (Core §7.1.1) | incremental on `rdf:type`; scoped on a subclass change |
| `sh:condition` | 3 | SHACL-AF rule's condition | not evaluated (ADR 0149) |
| `sh:construct` | 78 | SHACL-AF rule's query | not evaluated (ADR 0149) |
| `sh:datatype` | 133 | value type (Core §7.1.2) | incremental |
| `sh:declare` | 6 | prefix declarations (SHACL-SPARQL §2), the composition's | — |
| `sh:defaultValue` | 1 | value nodes of a property shape (Core §6.9.2) | incremental |
| `sh:description` | 37 | non-validating (Core §8.1) | static |
| `sh:in` | 103 | other (Core §7.9.3) | incremental |
| `sh:maxCount` | 354 | cardinality (Core §7.2.2) | incremental |
| `sh:maxInclusive` | 1 | value range (Core §7.3.4) | incremental |
| `sh:message` | 45 | message (Core §3.1.5) | static |
| `sh:minCount` | 274 | cardinality (Core §7.2.1) | incremental |
| `sh:minInclusive` | 2 | value range (Core §7.3.2) | incremental |
| `sh:minLength` | 1 | string-based (Core §7.4.1) | incremental |
| `sh:name` | 227 | non-validating (Core §8.1) | static |
| `sh:namespace` | 6 | prefix declarations (SHACL-SPARQL §2), the composition's | — |
| `sh:nodeKind` | 120 | value type (Core §7.1.3) | incremental |
| `sh:not` | 21 | logical (Core §7.7.1) | the class of the shapes referenced |
| `sh:object` | 5947 | SHACL-AF triple rule's object | not evaluated (ADR 0149) |
| `sh:or` | 111 | logical (Core §7.7.3) | the class of the shapes referenced |
| `sh:path` | 479 | predicate path (Core §4.1) | incremental |
| `sh:predicate` | 5947 | SHACL-AF triple rule's predicate | not evaluated (ADR 0149) |
| `sh:prefix` | 6 | prefix declarations (SHACL-SPARQL §2), the composition's | — |
| `sh:prefixes` | 159 | prefix declarations (SHACL-SPARQL §2) | — |
| `sh:property` | 479 | shape-based (Core §7.8.2) | the class of the shapes referenced |
| `sh:qualifiedMaxCount` | 4 | qualified value shape (Core §7.8.4) | the class of the shape referenced |
| `sh:qualifiedMinCount` | 2 | qualified value shape (Core §7.8.4) | the class of the shape referenced |
| `sh:qualifiedValueShape` | 4 | qualified value shape (Core §7.8.4) | the class of the shape referenced |
| `sh:qualifiedValueShapesDisjoint` | 2 | qualified value shape, with its siblings (Core §7.8.4) | the class of the shapes referenced |
| `sh:rule` | 6025 | SHACL-AF rule | not evaluated (ADR 0149) |
| `sh:select` | 3 | SPARQL-based constraint's query (SHACL-SPARQL §3) | — |
| `sh:severity` | 2 | severity (Core §3.1.4) | static |
| `sh:sparql` | 3 | SPARQL-based constraint (SHACL-SPARQL §3) | scoped when analysable (ADR 0171) |
| `sh:subject` | 5947 | SHACL-AF triple rule's subject | not evaluated (ADR 0149) |
| `sh:targetClass` | 77 | class target (Core §3.1.3.2) | incremental on `rdf:type`; scoped on a subclass change |
| `sh:targetObjectsOf` | 6 | objects-of target (Core §3.1.3.5) | incremental |
| `sh:targetSubjectsOf` | 9 | subjects-of target (Core §3.1.3.4) | incremental |

## Shapes that cannot be evaluated

None: the composed shapes graph loads with no problem.

Not evaluated, by design: the SHACL-AF rules (`sh:rule`, `sh:TripleRule`, `sh:SPARQLRule` and
their terms), which are inference and belong to milestone 9 (ADR 0149). A validation reads them as
triples of the shapes graph and nothing more.

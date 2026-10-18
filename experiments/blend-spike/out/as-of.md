# As of the commit that added Varve.Store.Azure -> Varve.Store

- Varve position: 69
- git commit: `c26624a 2026-10-15 feat(store): Varve.Store.Azure, the object-store contract over Blob Storage's REST API`
- `DiffAsync(68, 69)`: 77 asserted, 0 retracted; the reference among the asserted: yes

## R1, derived at this position

- <http://www.w3.org/1999/02/22-rdf-syntax-ns#type> <urn:varve-spike:blend#Constraint>
- <urn:varve-spike:lib#subject> <urn:varve-spike:pkg:Varve.Store.Azure>
- <urn:varve-spike:lib#relation> <urn:varve-spike:lib#mayReference>
- <urn:varve-spike:lib#object> <urn:varve-spike:pkg:Varve.Store>

## Rulings in force at this position

Accepted (an acceptance in the ledger graph as of the position):

```
?sets	?accepted
"140"^^<http://www.w3.org/2001/XMLSchema#integer>	"925"^^<http://www.w3.org/2001/XMLSchema#integer>
```

Filed and not accepted at this position, by set:

```
?set	?unaccepted
<urn:ledger-set:one-writer-by-an-epoch-and-a-log-slot-fence>	"10"^^<http://www.w3.org/2001/XMLSchema#integer>
<urn:ledger-set:storage-credentials-are-workload-identity>	"7"^^<http://www.w3.org/2001/XMLSchema#integer>
<urn:ledger-set:the-log-as-chunk-objects>	"8"^^<http://www.w3.org/2001/XMLSchema#integer>
<urn:ledger-set:the-object-store-contract>	"8"^^<http://www.w3.org/2001/XMLSchema#integer>
```

The rulings this reference rests on, and whether each was accepted here:

```
?d	?statement	?accepted
<urn:dec:varve/LayersStrictlyDownward>	"A package declares its layer, and a reference is legal only to a package in a strictly lower layer"	"true"^^<http://www.w3.org/2001/XMLSchema#boolean>
<urn:dec:varve/SameLayerReferenceIsViolation>	"A same-layer reference is a violation, not an exception: two packages in one layer that need each other are one package or two layers"	"true"^^<http://www.w3.org/2001/XMLSchema#boolean>
<urn:dec:varve/LayerTable>	"Seven layers: 0 Varve.Iri and Varve.Xsd, 1 Varve.Rdf, 2 the syntaxes and Varve.Sparql, 3 Varve.Sparql.Evaluation and Varve.Shacl, 4 Varve.Store, 5 integrations, 6 hosts"	"true"^^<http://www.w3.org/2001/XMLSchema#boolean>
<urn:dec:varve/ProvidersAreTheirOwnPackages>	"Each provider's REST and signing code is a layer-5 package of its own referencing only Varve.Store; Varve.Store.ObjectStorage implements IStorage over IObjectStore and knows no provider; the host composes the two"	"false"^^<http://www.w3.org/2001/XMLSchema#boolean>
<urn:dec:varve/BackendContractsNameTheStore>	"The object-storage packages' contracts may name Varve.Store's types: they exist only to implement its contracts, so every consumer of them already depends on it"	"false"^^<http://www.w3.org/2001/XMLSchema#boolean>
<urn:dec:varve/FiveOperations>	"The object-store contract is IObjectStore in Varve.Store beside IStorage: create-if-absent, replace-if-unchanged against an opaque version, ranged read, list by prefix, and delete, and nothing else"	"false"^^<http://www.w3.org/2001/XMLSchema#boolean>
```

`ProvidersAreTheirOwnPackages` is first accepted at position 81, git `6c3dd7c 2026-10-17 Accept ADRs 0134-0142 and the Object storage amendments`.

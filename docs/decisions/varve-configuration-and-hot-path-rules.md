---
set: varve-configuration-and-hot-path-rules
namespace: varve
adr: 0064
decisions:
  - key: ArchFamilyIsVarve
    statement: "ArchFamily is Varve, set once in Directory.Build.props"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: LayerDeclaredPerProject
    statement: "Each project declares ArchLayer in its own file, emitted as a generated assembly-level ArchLayer attribute, replacing VarveLayer and AssemblyMetadata"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: UnlayeredAssemblies
    statement: "Test assemblies and Varve.Analyzers declare no layer, and every other Varve project declares one"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: CompositionRootFlagAtLayer6
    statement: "ArchCompositionRoot is true on every layer 6 project and nowhere else"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ContractTypeVocabulary
    statement: "Contracts name only the BCL, the assembly's DomainModel namespaces, Contract types and Varve.Rdf, Varve.Iri and Varve.Xsd, with Varve.Sparql added in layer-3 project files only"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: ContractVocabularyWidenedPerProject
    statement: "Any further widening of ArchContractTypeAssemblies is in that project's own file with its reason, never global"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: DomainModelNamespaces
    statement: "The DomainModel namespaces are Varve.Iri, Varve.Xsd, Varve.Rdf, Varve.Sparql.Algebra and Varve.Store.Log, with Varve.Shacl.Reports planned"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: VarveRulesUnderDd0008
    statement: "dd_rule_id_prefixes is VARVE, and dd_banned_names is left at the package's default list"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: BannedDynamicAndReflectionMembers
    statement: "The banned-symbols list bans Microsoft.CSharp.RuntimeBinder and the reflection members that inspect or invoke, and not the System.Reflection namespace"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: NoLinqInLayersZeroToFour
    statement: "System.Linq.Enumerable is banned in projects at layers 0 to 4, test assemblies excepted"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HotPathDiscipline
    statement: "VARVE0003: a HotPath member may not box, capture, allocate arrays or reference types, concatenate strings, make params calls, use LINQ, foreach over a class enumerator, be async, or call a non-HotPath member outside the BCL allow-list"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HotPathAllowListIsConfiguration
    statement: "The hot-path BCL allow-list is configuration in .editorconfig, not code"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HotPathSignature
    statement: "VARVE0004: a HotPath member takes and returns no IEnumerable, no Task and no interface other than a Contract type itself marked HotPath"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: HotPathAttributeFromGenerator
    statement: "The hot-path attribute is the generated DecisionDriven.HotPathAttribute citing a decision, and eng/HotPathAttribute.cs is retired"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-25T00:00:00Z
  - key: PackableAssemblyDeclaresLayer
    statement: "VARVE0005: a Varve project that is neither a test assembly nor Varve.Analyzers declares ArchLayer, and a packable one always does"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
  - key: ExecutableLayerAndRootAgree
    statement: "VARVE0005: an executable not at layer 6, a library at layer 6, and an ArchCompositionRoot that disagrees with layer 6 are each reported"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-09-26T00:00:00Z
---

The rulings of [ADR 0064](../adr/0064-varve-configuration-and-hot-path-rules.md) still in force, one line each. The ADR is the
narrative; this is what code cites. Acceptance is transcribed from the ADR's Status (ADR 0062).

Received from earlier sets: `LayerDeclaredPerProject` and `UnlayeredAssemblies` from 0003's;
`ContractTypeVocabulary` (VARVE0007 as amended), `HotPathDiscipline` (VARVE0006) and
`PackableAssemblyDeclaresLayer` (the 2026-09-21 amendment) from 0004's;
`HotPathAttributeFromGenerator` from 0026's. Keys dated 2026-09-26 are the maintainer's
decisions on the pull request, before the ADR merged: VARVE0005, and the supersession of
0003's declaration mechanism.

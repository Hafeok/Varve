---
set: json-ld-over-utf8-json
namespace: varve
adr: 0112
decisions:
  - key: Utf8JsonReaderBuildsTheTree
    statement: "Varve.JsonLd reads a document with Utf8JsonReader into its own index-linked tree in pooled arrays, strings unescaped into one UTF-8 arena and names interned by their bytes, never through JsonDocument or JsonNode: expansion needs the whole document and the tree is its one copy"
  - key: Utf8JsonTypesAdmittedOnTheHotPath
    statement: "The hot-path allow-list admits System.Text.Json's Utf8JsonReader, Utf8JsonWriter, JsonTokenType, JsonReaderState, JsonReaderOptions and JsonWriterOptions, and System.Buffers' ReadOnlySequence and BuffersExtensions, by type: the reader and the writer are the BCL's and allocate nothing per token the package does not ask for, and the sequence is the caller's input"
  - key: ExpansionWritesIntoTheTree
    statement: "The expansion algorithm writes its result as new nodes of the same tree, so that an expanded document costs arena growth and no object per node; the tree is reset per document"
  - key: ContextsAreNotPerQuad
    statement: "An active context and its term definitions are objects allocated once per @context met, scoped contexts included, and not per quad, as a prefix directive is in Turtle; the figure per quad excludes them and the spec page says so"
  - key: BlankNodesAreRelabelled
    statement: "Every blank node the toRdf path emits is labelled _:b followed by a counter, a document's own labels mapped to fresh ones in order of first appearance, as the specification's node map does; the dataset is the same up to isomorphism"
  - key: DirectionDefaultIsNative
    statement: "RdfDirection.Native is the default: a value with @direction and @language becomes RDF 1.2's directional language-tagged string, and one with @direction alone a plain string; the specification's i18n-datatype and compound-literal modes and its default of dropping the direction are options, a departure from the specification's default stated in json-ld.md and ADR 0112"
  - key: JsonLiteralsAreCanonical
    statement: "The lexical form of an rdf:JSON literal is the value in RFC 8785 JSON Canonicalization Scheme, and the sort of each object's members and the ECMAScript number formatting are the one allocation in proportion to a JSON literal, which is not a quad"
  - key: NumbersAreXsdCanonical
    statement: "A JSON number becomes xsd:integer where it has no fraction and no exponent and is below 1e21, and xsd:double otherwise, each in the XSD canonical form Varve.Xsd formats; a value with @type xsd:double or xsd:integer is coerced likewise"
  - key: RemoteContextsNeedALoader
    statement: "A remote context or @import is fetched only through the caller's document loader; with none, the default, it is the error loading remote context failed, and nothing in this package opens a connection"
  - key: TheFirstErrorEndsTheParse
    statement: "JSON-LD has no recovery unit: the first error ends the processing with the specification's error code, and the quads handed out before it stand"
  - key: ErrorsAreTheSpecificationsCodes
    statement: "JsonLdErrorCode is the specification's JsonLdErrorCode enumeration, one value per code with its text, so that a caller and the W3C suite name the same thing"
  - key: FromRdfBuffersTheDataset
    statement: "The fromRdf writer must see the whole dataset before it can detect a list or group a subject, so it buffers quads as indices into one term arena with terms interned by their bytes and serialises on Dispose; what that costs per quad is measured and stated"
  - key: CompactFlattenAndFrameAreOut
    statement: "Compaction, flattening and framing are not implemented at milestone 6b; the package exposes expand, toRdf and fromRdf, and the three are recorded in json-ld.md as out of scope with the reason"
  - key: TheWholeStreamIsRead
    statement: "The Stream entry point reads the whole stream into a pooled buffer before parsing, because a JSON-LD document is processed as a whole and nothing can be emitted before its last byte"
  - key: LanguageTagsAreLowercased
    statement: "A language tag met in @language, in a language map or in an RDF literal is lowercased on the way through, as the specification permits and its suite's expected files assume"
  - key: TheQuadHandlerIsTheCallers
    statement: "The toRdf path hands each quad to the caller's delegate as a view valid for the call, and what the delegate does is the caller's cost"
  - key: TheProcessorIsKeptPerThread
    statement: "The processor's name table, tree and arenas are kept per thread and reset per document, as ArrayPool keeps its buffers, so that a steady state of documents allocates only what grows; a caller never sees the instance"
  - key: ProcessingModeIsOnePointOne
    statement: "The processor is JSON-LD 1.1 only: there is no json-ld-1.0 processing mode, @version 1.1 is accepted and never conflicts, and the suite's 1.0-only and generalized-RDF cases are excluded by a stated rule rather than exempted one by one"
---

The rulings of [ADR 0112](../adr/0112-json-ld-over-utf8-json.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone 6b of #10 (ADR 0066); acceptance
is the maintainer's act on the pull request.

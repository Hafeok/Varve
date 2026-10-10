---
set: rdf-xml-over-system-xml
namespace: varve
adr: 0122
decisions:
  - key: XmlReaderDoesTheXml
    statement: "Varve.RdfXml reads RDF/XML over System.Xml.XmlReader with DTD processing prohibited, no resolver, comments and processing instructions kept and characters checked, and does the RDF/XML grammar over the events it reports"
  - key: XmlWriterDoesTheXml
    statement: "Varve.RdfXml writes RDF/XML over System.Xml.XmlWriter into the caller's IBufferWriter of byte, UTF-8 without a byte order mark, indented by default"
  - key: XmlReaderAdmittedOnTheHotPath
    statement: "The hot-path allow-list admits System.Xml.XmlReader, IXmlLineInfo, XmlNodeType and XmlWriter by type: the reader's names are atomised and its values read in chunks, and what it allocates per element is measured and stated rather than hidden"
  - key: PositionsAreTheXmlReaders
    statement: "An RDF/XML error's position is the XML reader's 1-based line and 1-based column counted in UTF-16 characters, with no byte offset, because the reader decodes the document's encoding before this package sees a character"
  - key: XmlLiteralsAreCanonicalised
    statement: "The lexical form of an rdf:parseType=Literal element is its content in Exclusive XML Canonicalization 1.0 with comments, written from the reader's events, and the lists that sort an element's attributes and track rendered namespaces are the one allocation in proportion to a literal"
  - key: IdsAreKeptAsStrings
    statement: "Every rdf:ID resolved in a document is kept as a string in a set so that a duplicate is refused; the set is bounded by the document's IDs, not its triples"
  - key: FreshBlankNodesAreNumbers
    statement: "A blank node the RDF/XML reader invents is labelled with a number alone, which no rdf:nodeID can spell because an NCName cannot begin with a digit; the writer prefixes such a label with b"
  - key: NoRecoveryUnit
    statement: "RDF/XML has no recovery unit: the first error ends the parse and the triples handed out before it stand, because an ill-formed XML document has no well-defined continuation"
  - key: PredicateNamesAreCached
    statement: "The RDF/XML writer splits each distinct predicate into namespace, local name and prefix once and caches the strings by the predicate's bytes, so that no string is made per triple"
  - key: WriterRefusesWhatItCannotSpell
    statement: "The RDF/XML writer refuses by name a quad with a graph label, a triple term as subject or predicate, a predicate with no NCName suffix, and a term holding a character XML 1.0 cannot carry"
  - key: StringNamesAreCompared
    statement: "The hot-path allow-list admits String's Equals, its equality operators and Length by member, for comparing the XML reader's atomised element and attribute names, which allocates nothing"
  - key: TheCallbackIsTheCallers
    statement: "The RDF/XML reader hands each triple to the caller's delegate as a view valid for the call, and what the delegate does is the caller's cost"
  - key: StreamAdaptersAreOneWay
    statement: "The stream adapters over memory, a sequence and a buffer writer honour the one direction they exist for and throw NotSupportedException from Stream's other members, which is Stream's own contract for a non-seekable one-way stream"
  - key: ScopeAttributesAreCopied
    statement: "The values of xml:base, xml:lang and its:dir are copied out of the arena when met, because they outlive the top-level node element the arena is reset for; a scope attribute happens once per element and not once per triple"
  - key: BrowserSizeRevisit
    statement: "System.Private.Xml joins the browser build through this package; the measured growth is stated in the ADR, and a growth above 1.5 MB compressed reopens the hand-written reader the ADR rejected"
---

The rulings of [ADR 0122](../adr/0122-rdf-xml-over-system-xml.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone 6b of #10 (ADR 0066); acceptance
is the maintainer's act on the pull request.

---
set: rdf-1-2-turtle-and-trig
namespace: varve
adr: 0110
decisions:
  - key: Rdf12SyntaxIsAccepted
    statement: "The Turtle and TriG reader accepts RDF 1.2 as one grammar that is RDF 1.1's superset: reified triples, triple terms, annotations, reifiers, the version directive and LANG_DIR, gated by the rdf12 rdf-turtle and rdf-trig suites"
  - key: EditionDecidesSurrogateEscapes
    statement: "Where RDF 1.1 and RDF 1.2 contradict each other, on a character written as two \\u escapes forming a surrogate pair, an RdfVersion option on the parse decides, and the default is RDF 1.2, which refuses every escape naming a surrogate code point"
  - key: VersionDirectiveWrittenWhenNeeded
    statement: "The Turtle and TriG writer writes VERSION 1.2 once, immediately before the first statement carrying a construct RDF 1.1 cannot spell, or at the top when asked, and never leaves it silently absent from a document that needs it"
  - key: TripleTermIsAnObjectOnly
    statement: "The writer refuses by name a quad whose subject or predicate is a triple term, because RDF 1.2 Turtle gives a triple term one position"
  - key: RecoveryDepthCountsRdf12Brackets
    statement: "ADR 0030's recovery rule stands with its depth counted over the RDF 1.2 brackets << >>, <<( )>> and {| |} as well as [ ], ( ) and { }"
  - key: TripleTermComponentsByHandle
    statement: "IQuadSource answers a triple term's component handles through TryGetTripleTermComponents; the default answers through TryExternalise and TryInternalise, which is the path a consumer took before, and a source whose dictionary holds composite entries answers from the entry, so a blank node inside a triple term keeps the identity ADR 0044 gives it"
---

The rulings of [ADR 0110](../adr/0110-rdf-1-2-turtle-and-trig.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone 6b of #10 (ADR 0066); acceptance
is the maintainer's act on the pull request.

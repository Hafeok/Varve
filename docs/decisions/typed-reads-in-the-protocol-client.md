---
set: typed-reads-in-the-protocol-client
namespace: varve
adr: 0203
decisions:
  - key: TypedReadsAnswerAResult
    statement: "A typed read of Varve.Protocol.Client answers a VarveResult: the value, the problem, or both when a problem's body is the resource, always with the response's facts"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: AnAnswerIsNeverAnException
    statement: "An answer from the server is a value, as SparqlHttpClient's request methods answer every status; exceptions are for no answer: a timeout, a token known to have expired, a body over the cap"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ProblemIsReadWhole
    statement: "A problem is its type as a ProblemType, title, detail, status, instance as a RequestId, and every other member as sent; the client fixes no catalogue of problems"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ProblemTextIsDisplayText
    statement: "A problem's title and detail, readiness's detail and a dataset's reason are strings: text a person reads, which nothing compares, parses or routes on, because the type is the half a program reads"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ResponseFactsFromHeaders
    statement: "A response's facts are its status, Varve-Position and Varve-Management-Position as FeedPosition, and Varve-Request-Id or else the problem's instance; a position the response does not name is null"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ServerVocabularyIsAWrapper
    statement: "A role, a state or a bootstrap state is a wrapper over the string the server writes with the known values as static members, and a value the client does not know is carried as written"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: HealthClientNeedsNoToken
    statement: "VarveHealthClient reads /health/live and /health/ready with no token, and a 503 not-ready answers a Readiness beside its problem"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: ManagementClientHoldsItsOwnToken
    statement: "VarveManagementClient reads the routes under /manage/ with its own BearerTokenProvider for the management audience, so one provider is one audience and no caller needs a token naming both planes"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: NothingIsRemoved
    statement: "The typed reads add to the client's baseline and remove nothing; SparqlHttpClient's dataset-scoped management methods stay"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-28T00:00:00Z
  - key: StateChangeCarriesIfMatch
    statement: "SetStateAsync takes the version of the declaration graph the caller read and sends it as If-Match, so a close or an open never overwrites a newer declaration blind"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: ShapesBindingsByManagementQuery
    statement: "ShapesBindingsAsync reads a dataset's shapes bindings and the shapes graphs they bind through the management plane's SPARQL endpoint, with the dataset's name escaped into the graph's IRI"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
  - key: GrantReadKeepsClaimTypeAndMatch
    statement: "A grant read keeps a group grantee's claim type as a ClaimType and each graph template's match as a TemplateMatch, in GraphScope.TemplateMatches beside the templates"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-30T00:00:00Z
---

The rulings of [ADR 0203](../adr/0203-typed-reads-in-the-protocol-client.md), one line each. Filed
unaccepted by Web UI part 1 (#167); every citation is `CS0618` until the maintainer accepts them
(ADR 0066).

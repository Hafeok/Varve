---
set: varve-protocol-client
namespace: varve
adr: 0103
decisions:
  - key: ClientIsALayer5Library
    statement: "Varve.Protocol.Client, layer 5, is the HTTP client over the BCL's HttpClient: SparqlHttpClient, HttpServiceHandler, RdfDocumentClient, EndpointPolicy and ClientLimits, referencing neither Varve.Protocol nor Varve.Sparql.Store"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: EndpointPolicyAllowListDefaultNone
    statement: "Every outbound address is checked against an allow-list of IRI prefixes before a connection is opened, and the default list is empty, so SERVICE and LOAD are refused until the operator opts in"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: PrivateAddressesRefusedByDefault
    statement: "Only http and https; loopback, link-local, private and unspecified IP literals and localhost names are refused unless AllowPrivateAddresses is set; names are not resolved by the policy and credentials in the authority are refused"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: RedirectsNotFollowed
    statement: "A 3xx answer is a failure of the request, because a redirect is an address the policy did not see"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ClientLimitsBoundEveryRequest
    statement: "Every request is bounded by a host-configured timeout and a cap on response bytes, beyond which the response is a failure and never a truncated answer"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
  - key: ClientModelNamespace
    statement: "Varve.Protocol.Client.Model holds the client's public data: RdfDocument and EndpointRefusedException; the root namespace holds the clients, the handler and the policy, and SparqlHttpClient answers raw responses the caller streams"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-09T00:00:00Z
---

The rulings of [ADR 0103](../adr/0103-varve-protocol-client.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

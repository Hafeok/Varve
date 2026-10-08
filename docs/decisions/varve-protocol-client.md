---
set: varve-protocol-client
namespace: varve
adr: 0102
decisions:
  - key: ClientIsALayer5Library
    statement: "Varve.Protocol.Client, layer 5, is the HTTP client over the BCL's HttpClient: SparqlHttpClient, HttpServiceHandler, RdfDocumentClient, EndpointPolicy and ClientLimits, referencing neither Varve.Protocol nor Varve.Sparql.Store"
  - key: EndpointPolicyAllowListDefaultNone
    statement: "Every outbound address is checked against an allow-list of IRI prefixes before a connection is opened, and the default list is empty, so SERVICE and LOAD are refused until the operator opts in"
  - key: PrivateAddressesRefusedByDefault
    statement: "Only http and https; loopback, link-local, private and unspecified IP literals and localhost names are refused unless AllowPrivateAddresses is set; names are not resolved by the policy and credentials in the authority are refused"
  - key: RedirectsNotFollowed
    statement: "A 3xx answer is a failure of the request, because a redirect is an address the policy did not see"
  - key: ClientLimitsBoundEveryRequest
    statement: "Every request is bounded by a host-configured timeout and a cap on response bytes, beyond which the response is a failure and never a truncated answer"
  - key: ClientModelNamespace
    statement: "Varve.Protocol.Client.Model holds the client's public data: RdfDocument, EndpointRefusedException and the admin responses; the root namespace holds the clients, the handler and the policy"
---

The rulings of [ADR 0102](../adr/0102-varve-protocol-client.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

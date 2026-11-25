PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :     <https://example.org/learn/>

INSERT DATA {
  :room-9-99 a rec:Room ;
    rdfs:label "9.99 Nowhere" ;
    rec:capacity :room-9-99-capacity .
  :room-9-99-capacity rec:seatingCapacity "ten" .
}

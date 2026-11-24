PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>
PREFIX :     <https://example.org/learn/>

INSERT DATA {
  :room-3-03 a rec:Room ;
    rdfs:label "3.03 Phone booth" ;
    rec:area :room-3-03-area ;
    rec:capacity :room-3-03-capacity .
  :room-3-03-area a rec:ArchitectureArea ;
    rec:netArea "3.0"^^xsd:float .
  :room-3-03-capacity a rec:ArchitectureCapacity ;
    rec:seatingCapacity 1 .
}

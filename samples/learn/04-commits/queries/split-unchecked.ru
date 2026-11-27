PREFIX rec:  <https://w3id.org/rec#>
PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX xsd:  <http://www.w3.org/2001/XMLSchema#>
PREFIX :     <https://example.org/learn/>

# 1. Halve room 3.02, and make the other half room 3.04.
DELETE {
  :room-3-02 rdfs:label ?name .
  :room-3-02-area rec:netArea ?area .
  :room-3-02-capacity rec:seatingCapacity ?seats .
}
INSERT {
  :room-3-02 rdfs:label "3.02 Open office, east" .
  :room-3-02-area rec:netArea ?half .
  :room-3-02-capacity rec:seatingCapacity ?halfSeats .

  :level-3 rec:hasPart :room-3-04 .
  :room-3-04 a rec:Room ;
    rdfs:label "3.04 Open office, west" ;
    rec:area :room-3-04-area ;
    rec:capacity :room-3-04-capacity .
  :room-3-04-area a rec:ArchitectureArea ;
    rec:netArea ?half .
  :room-3-04-capacity a rec:ArchitectureCapacity ;
    rec:seatingCapacity ?halfSeats .
}
WHERE {
  :room-3-02 rdfs:label ?name .
  :room-3-02-area rec:netArea ?area .
  :room-3-02-capacity rec:seatingCapacity ?seats .
  BIND (?area / 2 AS ?half)
  BIND (?seats / 2 AS ?halfSeats)
} ;

# 2. The temperature sensor is on the west wall: it moves to 3.04.
DELETE DATA { :room-3-02 rec:hasPoint :point-temp-3-02 } ;
INSERT DATA { :room-3-04 rec:hasPoint :point-temp-3-02 }

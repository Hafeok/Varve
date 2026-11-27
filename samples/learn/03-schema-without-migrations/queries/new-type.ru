PREFIX brick: <https://brickschema.org/schema/Brick#>
PREFIX rec:   <https://w3id.org/rec#>
PREFIX rdfs:  <http://www.w3.org/2000/01/rdf-schema#>
PREFIX room:  <https://example.org/rec/room/>
PREFIX point: <https://example.org/rec/point/>

INSERT DATA {
  point:co2-level-200 a brick:CO2_Level_Sensor ;
    rdfs:label "CO2 level, room 200" .
  room:200 rec:hasPoint point:co2-level-200 .
}

PREFIX rdfs: <http://www.w3.org/2000/01/rdf-schema#>
PREFIX :     <https://example.org/learn/>

DELETE { :room-3-01 rdfs:label ?name }
INSERT { :room-3-01 rdfs:label "3.01 Harbour, with display" }
WHERE  { :room-3-01 rdfs:label ?name }

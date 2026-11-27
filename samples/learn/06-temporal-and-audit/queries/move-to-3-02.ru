PREFIX rec: <https://w3id.org/rec#>
PREFIX :    <https://example.org/learn/>

DELETE { :asset-display-3-01 rec:locatedIn ?room }
INSERT { :asset-display-3-01 rec:locatedIn :room-3-02 }
WHERE  { :asset-display-3-01 rec:locatedIn ?room }

# The REC terms, by the chapter that introduces them

The learning track uses one domain, a RealEstateCore portfolio
([ADR 0141](../adr/0141-realestatecore-as-the-reference-domain.md)), and
introduces its vocabulary a little at a time. A chapter uses only the `rec:`
and `brick:` terms of its own row and the rows before it. `eng/learn.cs`
holds every page and every sample to this table: a term used before its
chapter, or in no row, fails the `learn` job
([ADR 0249](../adr/0249-the-learning-tracks-pages-quote-what-ci-ran.md)).

`rdf:`, `rdfs:` and `xsd:` are RDF's own vocabularies, not REC's, and appear
from chapter 1 without a row.

| Chapter | Terms |
|---:|---|
| 1 | `rec:Room`, `rec:area`, `rec:ArchitectureArea`, `rec:netArea`, `rec:capacity`, `rec:ArchitectureCapacity`, `rec:seatingCapacity` |
| 2 | `rec:Site`, `rec:Building`, `rec:Level`, `rec:levelNumber`, `rec:hasPart`, `rec:isPartOf`, `rec:Asset`, `rec:locatedIn`, `rec:isLocationOf`, `rec:hasPoint`, `brick:Temperature_Sensor`, `brick:CO2_Sensor` |
| 3 | `rec:Region`, `rec:Space`, `rec:Architecture`, `rec:Office`, `rec:OfficeLandscape`, `rec:PhoneBooth`, `rec:ConferenceRoom`, `rec:Bathroom`, `rec:Hallway`, `rec:Kitchenette`, `rec:Pantry`, `rec:Cabinet`, `rec:Desk`, `rec:Lamp`, `rec:OfficeChair`, `rec:IAQSensorEquipment`, `rec:OccupancySensorEquipment`, `brick:Entity`, `brick:Class`, `brick:Point`, `brick:Sensor`, `brick:Setpoint`, `brick:Air_Quality_Sensor`, `brick:Humidity_Sensor`, `brick:Occupancy_Count_Sensor`, `brick:Air_Temperature_Setpoint`, `brick:CO2_Level_Sensor` |

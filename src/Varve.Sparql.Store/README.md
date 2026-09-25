# Varve.Sparql.Store

SPARQL 1.1 Update over a Varve store, **one request, one commit**.

- **Atomic and sequential.** A request is pinned at the readable head; each
  operation is evaluated over the overlay of the ones before it, so later
  operations see earlier ones' effects, including the terms they created; the
  composed change is submitted as one commit that expects the pinned
  position. A request that changes nothing makes no commit.
- **Conflicts are returned, not retried** — unless `ConflictRetries` says so,
  because re-running a read-decide-write request against a newer head is the
  caller's decision.
- **Validators are the dataset's**, and run as part of the commit.
- **No empty graphs.** A named graph exists when it holds a quad, which SPARQL
  1.1 Update §3.2 allows: `CREATE` records nothing, `DROP` and `CLEAR` retract.
- **`LOAD` through a contract.** `ILoadSource` resolves an IRI to a document;
  the default refuses, and the HTTP source is the server's.

```csharp
Update update = SparqlParser.ParseUpdate("""
    PREFIX : <http://example.org/>
    INSERT DATA { :a :p 1 } ;
    DELETE { ?s :p ?o } INSERT { ?s :p 2 } WHERE { ?s :p ?o }
    """u8);

CommitResult result = await SparqlUpdate.ExecuteAsync(dataset, update, new UpdateOptions());
```

Layer 5 of [Varve](https://github.com/Hafeok/Varve), a .NET-native
event-sourced RDF store and SPARQL toolkit. MPL-2.0.

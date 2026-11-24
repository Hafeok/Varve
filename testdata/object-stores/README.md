# Object stores: the providers and their result records

The evidence behind [`docs/operator/object-stores.md`](../../docs/operator/object-stores.md)
([ADR 0183](../../docs/adr/0183-object-store-verification-and-the-compatibility-page.md)).

- [`providers.yaml`](providers.yaml) lists the stores the page shows. Its
  header says what each field means.
- `results/<provider>-<server-version>.yaml` is one run of
  `varve storage verify --record` against that provider at that version.

**To add a record:** run the command against your store, put the file it
writes under `results/`, run `dotnet run eng/object-stores.cs`, and open a
pull request with the record and the regenerated page. A provider that is
not in `providers.yaml` yet gets an entry in the same pull request.

```
varve storage verify s3://my-bucket --endpoint https://s3.example.org --region eu-central-1 \
  --record results/example-1.2.3.yaml --provider example --server-version 1.2.3 \
  --deployment "three nodes, eu-central-1" --signed-off-by "Your Name <you@example.org>"
```

A record is never edited by hand: it is what the command wrote. The
server's version and the deployment are your statement, because the store
does not say what it is.

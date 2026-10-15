# Varve.Store.S3

An S3-compatible object store for `Varve.Store.ObjectStorage` (ADR 0134):
the five object-store operations over the S3 REST API with Signature Version
4 and `HttpClient`, with no AWS SDK.

- **Conditional writes** by `If-None-Match: *` and `If-Match`. A server must
  honour both; the contract suite in Varve's tests decides which servers do
  (RustFS in CI).
- **Credentials are the workload's identity** (ADR 0137):
  `AssumeRoleWithWebIdentity` with the token in `AWS_WEB_IDENTITY_TOKEN_FILE`
  for the role in `AWS_ROLE_ARN`, read afresh at each refresh.
- **A static key only on loopback**, for a server on the same machine:
  `S3DevelopmentKey`, refused for any other endpoint. A typical NAS that
  offers neither web identity nor runs on the same host is outside both.

```csharp
using S3ObjectStore store = S3ObjectStore.Create(new S3ObjectStoreOptions
{
    Endpoint = new Uri("https://s3.example.net"),
    Bucket = new S3Bucket("dataset"),
    Region = new S3Region("eu-north-1"),
    Clock = TimeProvider.System,
});
```

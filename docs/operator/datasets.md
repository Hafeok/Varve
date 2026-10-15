# Datasets: their boundary, and their setup

A dataset is what a server serves under `/datasets/{name}/`. This page covers
two things:

- **where to draw a dataset's boundary**;
- **how an application declares a dataset's setup**, so that it can be applied
  on every start and commit nothing when nothing changed (ADR 0130).

The configuration keys are in [Configure](configure.md#datasets).

## Choosing the boundary

**A dataset is the unit of:**

- **consistency:** one sequencer, one total order;
- **history:** one log, positions and as-of reads;
- **lease:** one writer process;
- **throughput:** commits are serialised per dataset;
- **lifecycle:** created, opened, closed and deleted whole;
- **erasure:** keys and subjects are a dataset's.

Two things that must change together, or be read as of the same position, are
in one dataset. Two things that need neither are better apart, where neither
waits for the other's commits.

**Graphs are the partition inside one history**, and the unit of scope. Grants
are by graph (ADR 0107). A feed can be filtered to a graph (ADR 0097). A
baseline adds to its graph and no other.

In domain-driven design terms:

- **A dataset is a bounded context:** one vocabulary, one writer,
  integration with others through its commits feed
  ([Integrate](integrate.md)).
- **A tenant is a shard of a context, not a context.** Each tenant's dataset
  carries the same vocabulary.
- **A second context within a tenant is a second dataset.**
- **The setup baseline keeps a context's vocabulary identical across its
  shards:** one setup, applied to each tenant's dataset.

## The setup

`Varve.Protocol.Client`'s `DatasetSetup` declares four things:

1. **The dataset:** its name, and its storage when it is created.
2. **Its settings:** the keys of the settings resource (ADR 0118) the setup
   means to hold.
3. **Its baseline:** sources of RDF, each added to one graph by the Graph
   Store Protocol.
4. **Its migrations:** the changes that are not idempotent by nature, such as a
   retraction, a rename or a split. Each runs once.

`client.ApplyAsync(setup)` makes the dataset so, in that order. Applying it
again commits nothing, since re-asserting what is there is an empty delta (I2).

- **The dataset** is created with `PUT /datasets/{name}` when a read answers
  `404`, which takes a server admin. A dataset that exists needs only the
  permissions of the steps after it.
- **The settings are desired state, and the last writer wins.**
  - Each key the setup declares is compared with the dataset's. When one
    differs, the declared keys are merged in under `If-Match`, which takes the
    dataset's `admin`.
  - **A setup overwrites a manual change to a setting it declares**, on the next
    apply.
  - **It leaves a setting it does not declare alone**, whatever its value. To
    let operators own a setting, leave it out of the setup.
  - A settings commit names an agent, so it is refused in anonymous mode.
- **A baseline source is added**, with the Graph Store Protocol's `POST`,
  **and never replaces its graph.**
  - What the application writes to the same graph survives every apply and
    every retry.
  - A triple dropped from a baseline's source is not retracted by the next
    apply. Retract it with a migration, which is recorded.
  - **A source holding a blank node is refused before anything is sent.** A
    blank node in a request is a new node every time (ADR 0098), so the
    baseline would commit on every apply. Name the node with an IRI: one of
    the application's own, or a skolem IRI such as
    `https://example.org/.well-known/genid/1` (RDF 1.1 Concepts §3.5).
- **A migration** is one SPARQL Update, sent together with its own record as
  one request under `If-Match`: one commit, or none.
  - **The record** is `<https://w3id.org/varve/ns#setup/migration/{id}> a
    varve:Migration ; varve:migrationId "{id}"`, in the graph
    `https://w3id.org/varve/ns#setup` (`varve:setup`).
  - **Its position** is the commit that asserted it. Find it in the commits
    list filtered to `varve:setup`, or as of any position.
  - **Two appliers at once:** when another writer came first (`409` or
    `412`), the records are read again, so two appliers starting together
    apply each migration once.
  - **A migration that fails** commits nothing, and its record is not written.
    The migrations before it stand.

The applier needs `write` on every graph the setup writes, `varve:setup`
included, and `read` on `varve:setup`. **A setup never grants permissions:**
an application cannot grant itself any, and grants are the server's
configuration ([Authenticate](authenticate.md)).

### In an application: `AddVarve`

```csharp
builder.Services.AddVarve(options =>
{
    options.Server = new Uri("https://varve.example/");
    options.Setup = DatasetSetupFile.Read("varve/orders.yaml");   // or a DatasetSetup built in code
    options.Tokens = services => /* the application's client-credentials provider */;
    options.Clock = TimeProvider.System;
});
```

This registers a `SparqlHttpClient` for the dataset, and a hosted service that
applies the setup as the host starts. **A setup that cannot be applied fails
the start**, with the step, the status and the problem body (ADR 0131).

### From a pipeline: `varve setup apply`

```sh
varve setup apply varve/orders.yaml https://varve.example/ --authority https://login.example/ --client-id deployer --client-secret "$SECRET"
```

It takes the server's address, or the dataset's on it, and the token options
every remote command takes. It prints what committed, or that nothing did. It
exits 1 when the server refused, with the problem, and 2 when the file is
outside the subset below, with its line and column.

### In an Aspire AppHost: `WithDataset(…, setup:)`

```csharp
builder.AddVarve("varve")
    .WithOidc(authority, "api://varve")
    .WithSetupClient("varve-setup", setupSecret)
    .WithDataset("orders", read: ["Varve.Read"], write: ["Varve.Write"], setup: "varve/orders.yaml");
```

The AppHost runs `varve setup apply` from the server's own image once the server is
healthy, as the container `{server}-setup-{dataset}`. Another resource waits
for it with `.WaitForDatasetSetup(varve, "orders")`.

## The setup file

A setup file is YAML, **of a strict subset**, read without a YAML package.
**Anything outside the subset is refused with its line and column**, never
accepted silently.

```yaml
# The shop's orders dataset.
dataset: orders                  # required: the dataset's name
storage: File                    # File (default) or Memory: used when it is created
settings: |                      # optional: the settings resource's JSON
  {"defaultAccessScope": "Current"}
baseline:                        # optional, in order
  - name: vocabulary             # optional: defaults to the file's name
    file: vocabulary.ttl         # required: relative to this file
    graph: https://shop.example/vocabulary   # an IRI, or `default`
    format: text/turtle          # optional when the extension says it
migrations:                      # optional, in order, each once
  - id: 2026-10-01-statuses      # required, unique, never reused
    update: |                    # one SPARQL Update request…
      DELETE { ?o <https://shop.example/ns#state> ?s } INSERT { ?o <https://shop.example/ns#status> ?s }
      WHERE { ?o <https://shop.example/ns#state> ?s }
  - id: 2026-10-02-split
    file: migrations/split.ru    # …or a file holding one
```

### The subset

| Construct | Read as |
|---|---|
| `key: value` | a mapping entry. A key is a letter, then letters, digits, `_` and `-`. A key is followed by `: ` and its value, or by `:` at the end of the line and a nested block. |
| indentation | spaces. A nested block is indented more than its key. Every entry of a mapping is at the same indentation. |
| `- key: value` | an item of a sequence, which is a mapping. Its other keys align with the first key after the `- `. |
| plain value | the rest of the line, trimmed, up to a ` #` comment. It may not start with `&`, `*`, `!`, `[`, `{`, `>`, `|`, `'`, `"`, `@`, `` ` `` or `%` unless it is one of the forms below. |
| `'single'` | the text between the quotes, `''` for one quote. Nothing but a comment follows it. |
| `"double"` | the text between the quotes, with the escapes `\"` `\\` `\/` `\n` `\t` `\r` and `\uXXXX`. Nothing but a comment follows it. |
| `\|` | a block scalar: the lines after, indented more than the key, with their common indentation removed and their line ends kept. Trailing empty lines are dropped, and one line end is kept. |
| `#` | a comment, starting a line or following a space. |

**Refused**, each at its line and column:

- a tab in indentation;
- an anchor (`&`), an alias (`*`) or a tag (`!`);
- a flow collection (`[…]` or `{…}`);
- a folded scalar (`>`), or `|` with an indicator (`|-`, `|+`, `|2`);
- a document marker (`---`, `...`) or a directive (`%`);
- a quoted or complex (`?`) key;
- a key outside the schema above, or a key given twice;
- a line indented more than its mapping.

A file's encoding is UTF-8. A baseline file may start with a byte order mark,
which is dropped before it is read or sent.

# Datasets: their boundary, their declaration, and their setup

A dataset is what a server serves under `/datasets/{name}/`. This page covers
three things:

- **where to draw a dataset's boundary**;
- **how a dataset is declared**: its existence, storage, settings, placement
  and grants, as code in the management plane (ADRs 0163, 0166);
- **how an application declares a dataset's data**, its setup, so that it can
  be applied on every start and commit nothing when nothing changed (ADR 0130).

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

## Declaring a dataset

A dataset exists because the fleet's management plane declares it (ADR 0163).
Its declaration is one named graph of the fleet's system dataset,
`varve:manage/dataset/{name}`, written in TriG in the `varve:` vocabulary
(ADR 0166). Every instance of the fleet reads the declarations at the
management position it has applied, and converges to them: it opens the
datasets placed on it as their writer, follows the others in the object store,
closes what is declared closed, and the writer deletes what is no longer
declared. A file kept in the application's repository is the source of
truth; `varve apply` makes the server so.

```trig
@prefix varve: <https://w3id.org/varve/ns#> .
@prefix n: <https://notes.example/manage/> .

<https://w3id.org/varve/ns#manage/dataset/notes> {
  n:notes a varve:Dataset ;
    varve:datasetName "notes" ;
    varve:storage varve:ObjectStorage ;      # File, Memory or ObjectStorage
    varve:defaultAccessScope varve:Current ; # a setting, converged by the writer
    varve:placedOn "varve-1" .               # optional: the system writer's by default

  n:users a varve:Role ; varve:claimValue "Notes.User" .
  n:support a varve:Group ; varve:claimType "groups" ; varve:claimValue "support-team" .

  # Each user writes the graphs under their own prefix, also on behalf of an application.
  n:own-graphs a varve:GraphTemplate ;
    varve:template "https://notes.example/users/{sub}/" ; varve:match varve:Prefix .
  n:users-own a varve:Grant ;
    varve:grantee n:users ; varve:permission varve:write ; varve:graphTemplate n:own-graphs .
  n:users-shared a varve:Grant ;
    varve:grantee n:users ; varve:permission varve:read ; varve:graph <https://notes.example/shared> .
  n:support-read a varve:Grant ;
    varve:grantee n:support ; varve:permission varve:read ; varve:graphPrefix "https://notes.example/users/" .
  n:owner-admin a varve:Grant ;
    varve:grantee <https://login.example/v2.0#owner> ; varve:permission varve:admin .
}

<https://w3id.org/varve/ns#manage/fleet> {
  <https://ops.example/manage/platform> a varve:Role ; varve:claimValue "Varve.Operator" .
  <https://ops.example/manage/ops> a varve:Grant ;
    varve:grantee <https://ops.example/manage/platform> ; varve:permission varve:operator .
}
```

**No blank nodes**: every node is an IRI, so a declaration means the same on
every apply. The code validator refuses an unknown term, a value of the wrong
kind, a term reserved for a later milestone, and, until the event-schema move
(ADR 0167), an event-catalogue term: the refusal is `422
declaration-invalid`, naming the graph, the node and the property.

### `varve apply`

```sh
varve apply notes.trig --server https://varve.example --plan --exit-code   # drift check: exit 2 on a diff
varve apply notes.trig --server https://varve.example --plan --out plan.json
varve apply --from-plan plan.json --server https://varve.example            # refused if a graph moved since
varve apply notes.trig --server https://varve.example                        # plan and commit
```

- **A file owns every graph it names, whole.** Two files naming one graph are
  refused.
- **The plan** is the canonical diff (RDFC-1.0) of each owned graph against
  the server's, at the system position it read, with each graph's version.
- **The commit** is one `POST /manage/apply` replacing every owned graph,
  under `Varve-If-Graphs-Match` naming each at the version the plan read.
  Applies to disjoint graphs never conflict; applies to the same graph do,
  and a stale saved plan is `412`.
- **Applying again** commits nothing.
- **`--from-config <appsettings.json>`** prints the TriG that replaces a
  configuration's `Varve:Datasets` and `Varve:Auth:Datasets`, and names its
  `Varve:Auth:Server:Admin` values for `Varve:Manage:BootstrapOperators`.
- **The token** is for the management audience; CI obtains one by workload
  identity federation ([Authenticate](authenticate.md#the-management-plane)).

### The management resources

The same graphs, one resource at a time, under the management plane's token:

| Resource | Methods |
|---|---|
| `/manage/datasets` | `GET`: the datasets the caller administers, each with its state, storage and this instance's role (`writer`, `follower` or `elsewhere`) |
| `/manage/datasets/{name}` | `GET` the declaration as TriG, its graph's version as `ETag`; `PUT` replaces it, `PATCH` replaces the values of each subject and property the body states; `DELETE` removes it, and only a dataset declared closed |
| `…/settings` | `GET` the declared settings as JSON; `PUT` and `PATCH` take TriG stating the dataset node's settings |
| `…/state` | `GET` this instance's state and role; `PUT {"state": "open"}` or `{"state": "closed"}` declares it, and every instance converges |
| `…/status`, `…/checkpoints` | the dataset's status, and a checkpoint, taken by its writer |
| `…/grants` | `GET` the grants as JSON, as of `Varve-As-Of`: who could read which graphs, then |
| `/manage/fleet` | the fleet graph, as the dataset resource; an operator's |
| `/manage/graphs` | `GET` every management graph as TriG, with `Varve-Graph-Versions` |
| `/manage/apply` | `POST`, as above |
| `/manage/sparql` | SPARQL over the system dataset, for an operator; an update runs the code validator |
| `/manage/system` | `GET` this instance's role in the system dataset, epoch, positions, bootstrap state and writer |

`PUT`, `PATCH` and `DELETE` take `If-Match` with the graph's version. A
management write to an instance that does not write the system dataset is
forwarded to the one that does ([The fleet](fleet.md)).

- **Deleting** is two steps: declare the dataset closed, then `DELETE` it,
  which is `409 dataset-open` before. Once the declaration is gone, the
  dataset's writer deletes its storage: all of it, and not undoable.
- **Settings converge.** Settings stay a commit kind of the dataset (ADR
  0021): when the declared settings differ from the dataset's, its writer
  commits a `Settings` commit carrying the management position it converged
  to (ADR 0165), with the writer instance as its agent.

## The setup

`Varve.Protocol.Client`'s `DatasetSetup` declares a dataset's data, two
things:

1. **Its baseline:** sources of RDF, each added to one graph by the Graph
   Store Protocol.
2. **Its migrations:** the changes that are not idempotent by nature, such as a
   retraction, a rename or a split. Each runs once.

The dataset's existence, storage and settings are not a setup's: they are
its declaration (ADR 0163 point 10). `client.ApplyAsync(setup)` makes the
dataset's data so, in that order. Applying it again commits nothing, since
re-asserting what is there is an empty delta (I2).

- **The dataset** must be declared. A setup never creates one: a dataset the
  management plane does not declare is refused at the first step, before
  anything is sent.
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
an application cannot grant itself any, and grants are declarations
([Authenticate](authenticate.md)).

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
    .WithOperators("Varve.Operator")
    .WithManagementClient("varve-deployer", deployerSecret, scope: "api://varve-manage/.default")
    .WithSetupClient("varve-setup", setupSecret)
    .WithDataset("orders", read: ["Varve.Read"], write: ["Varve.Write"], setup: "varve/orders.yaml");
```

The AppHost writes the declarations of every `WithDataset` to one TriG file,
and runs `varve apply` from the server's own image once the server is
healthy, as the container `{server}-declare`. It then runs `varve setup
apply`, as the container `{server}-setup-{dataset}`. Another resource waits
for it with `.WaitForDatasetSetup(varve, "orders")`.

## The setup file

A setup file is YAML, **of a strict subset**, read without a YAML package.
**Anything outside the subset is refused with its line and column**, never
accepted silently.

```yaml
# The shop's orders dataset.
dataset: orders                  # required: the dataset's name, declared in the management plane
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
- a key outside the schema above, or a key given twice; `storage` and
  `settings` are refused by name: they are a dataset's declaration now
  (ADR 0163);
- a line indented more than its mapping.

A file's encoding is UTF-8. A baseline file may start with a byte order mark,
which is dropped before it is read or sent.

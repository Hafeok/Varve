# Test fixtures

Version-controlled, and never fetched at test time.

## `recorded/` — scenarios and their exchanges

Each `<name>.json` is a scenario: a declaration, the command run on it, every
HTTP exchange with GitHub **in the order the tool must make them**, and the exit
code expected. `<name>.out` beside it is what the command printed (stdout,
stderr, the waits the rate-limit policy asked for, and for `export` the file it
wrote). `RecordedTests` replays each one; a request that differs from the
recording in method, path, query or JSON body fails the test, as does one that
is missing or extra.

- **`*-check`** — the diff engine over recorded live state, **one per resource
  kind**, each including something added by hand that the declaration does not
  name: a topic, a label, a ruleset, an environment secret, a repository secret,
  a Discussions category, a linked board, an allowed-actions pattern.
- **`*-apply*`** — the contract tests for the writes: each write endpoint the
  tool calls appears in at least one, with its exact request body.
- **`rate-limit-check`**, **`apply-stops-at-failure`**, **`export-all`**,
  **`action-check-clean`** — the retry policy, the stop-and-report behaviour,
  the export format, and the clean scenario the Action test replays to the
  native binary.

`Every_endpoint_and_operation_the_tool_uses_has_a_recorded_exchange` fails if
an endpoint in `Endpoints.All` or an operation in `GraphQlOperations.All` has no
exchange here; `Every_recorded_request_is_one_the_tool_catalogues` fails on a
recording that matches no catalogued endpoint.

### Where the recordings come from — read this

**They were not captured from a live repository.** The session that wrote the
tool ran it against none (#23). They are built from GitHub's own published
descriptions of the API, and each file's `source` says which:

- **REST responses** are the response examples in GitHub's OpenAPI description,
  [`github/rest-api-description`](https://github.com/github/rest-api-description)
  at commit `02e8fa6`, `descriptions/api.github.com/api.github.com.json`,
  verbatim where one exists. Where a scenario needed a state the example does
  not show — a security option switched off, a second ruleset, labels split over
  two pages — the example was changed as little as that needed, and the
  `source` says so.
- **Request bodies** are what the tool must send, written to the request schema
  of the same description.
- **GraphQL responses** have no published examples. They are constructed to
  GitHub's GraphQL schema (`schema.graphql` as published by
  [`octokit/graphql-schema`](https://github.com/octokit/graphql-schema)), with
  GitHub's default Discussions categories and default Status options.

So these prove the tool honours GitHub's *documented* contract. They cannot
prove GitHub honours it. The integration test closes that gap when it is run:
with `REPO_STANDARD_RECORD_DIR` set it writes every real exchange in this same
format, and a captured recording can replace a documented one file by file.

`REPO_STANDARD_ACCEPT=1 dotnet test` rewrites the `.out` files after a change
that is meant to change them. Read the diff before committing it.

## `schema-keys.json`

For every GET the tool makes, the dotted key paths of the 200 response schema in
the same OpenAPI description. `FakeConformanceTests` requires every field the
in-memory GitHub answers with to be one of them, at the same path — the fake
used by the round-trip property may not invent a field GitHub does not have.

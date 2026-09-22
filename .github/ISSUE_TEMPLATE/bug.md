---
name: Bug
about: Something behaves differently from how it is specified or documented
title: ""
labels: bug
assignees: []
---

## Problem

> [!TIP]
> What happened, and what you expected instead. Concretely.

## Reproduction

> [!TIP]
> **The most valuable thing in this issue.** For a parser, the document — the
> exact bytes, including line endings, since CR LF has been a real source of
> defects here. For the store, the sequence of operations.
>
> A test that fails is better still. The repository owns its test documents and
> never fetches them, so a case added here can become one.

```
paste the input, or attach the file
```

```csharp
// the smallest code that shows it
```

## What you observed

```
the actual output, error, or stack trace
```

## Environment

| | |
|---|---|
| Varve version | |
| .NET SDK | |
| Host | embedded / browser WASM / Native AOT |
| OS | |

## Specification

> [!TIP]
> If a W3C specification or one of `docs/spec/` says what should happen, cite
> the section. A citation turns an opinion about correctness into a fact about
> it, and it is usually what decides how the fix is written.

## Conformance

- [ ] I checked whether a W3C case in `tests/Varve.Conformance.Tests/baseline/passing.txt` covers this
- [ ] There is no case covering it, which may itself be the defect

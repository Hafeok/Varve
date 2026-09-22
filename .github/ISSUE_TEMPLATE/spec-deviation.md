---
name: Specification deviation
about: Varve and a specification disagree — or a specification disagrees with itself
title: ""
labels: spec-deviation
assignees: []
---

## Problem

> [!TIP]
> Which specification, which section, and what Varve does instead.
>
> This template exists separately from Bug because the answer is sometimes that
> the *specification* is wrong. `docs/spec/n-triples.md` records two such
> findings: RDF 1.1's `PN_CHARS_U` production contradicts its own test suite
> over the colon, and RDF 1.2 has since resolved it the way the suite already
> assumed.

**Specification and section:**

**What it requires:**

**What Varve does:**

## Evidence

> [!TIP]
> Quote the production or the prose. If the specification's own test suite
> disagrees with its text, say so and cite the case — that is the strongest
> possible evidence and it changes what the right fix is.

## Which is wrong

- [ ] Varve is wrong and should be fixed
- [ ] The specification is wrong or ambiguous, and Varve's behaviour should be recorded in `docs/spec/`
- [ ] Unclear, and this issue is the place to work it out

## Impact

> [!TIP]
> Does a W3C case cover it? Would fixing it change the conformance baseline, or
> require an exemption? A behaviour change that no case covers is the defect
> class the 3a close-out found, and it is worth naming as one.

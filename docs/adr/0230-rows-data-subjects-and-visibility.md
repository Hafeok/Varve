# 0230 — Rows, data subjects and visibility

## Status

**Filed unaccepted by milestone 9, part 1 (#202, under #14), 2026-10-30**
(ADR 0066). Acceptance is the maintainer's act on the pull request. Decided
by the maintainer on the plan in #14 (2026-10-30), decisions 1 and 3, and the
gap closed the same day: links into a hidden row. **Amends ADR
[0107](0107-graph-level-authorisation.md)** by a dated amendment in that file:
point 8's boundary is closed, and a caller's scope gains rows.

Records decision set
[`rows-data-subjects-and-visibility`](../decisions/rows-data-subjects-and-visibility.md).

## Context

ADR 0107 decides visibility per graph, by a lookup of the graph handle, and
leaves row-level access to milestone 9: "per subject, per classifier, per
value". It is in 1.0 and stable (#15, 2026-10-19), so it cannot wait for
erasure's external review, and must work with erasure mode off.

ADR 0023's classifier "assigns term occurrences to data subjects". That is
the store's only notion of whom data is about. Row-level access uses that
assignment without encryption. What must be decided is exactly which quads a
classification makes visible to whom. The decision must hold one property in
both directions, as ADR 0190's does for entailments:
- **no caller sees a quad about a subject it can't read**, through any surface;
- **every caller sees every quad it may read**.

It also has to resist **probing**. ADR 0190 point 7 removed the case where a
writer could learn a graph's content from whether its write was refused.
Rows invite the same attack: if `alice ex:knows x` were visible or hidden
depending on whether `x` is someone else's subject, a writer could learn that
by writing it.

## Decision

### 1. Classifier bindings and data subjects

- **A classifier binding** B classifies the quads of a graph set Γ_B, taken
  together as one data graph, as a shapes binding validates its graphs (ADR
  0174 point 5). It is a management declaration, `varve:ClassifierBinding`
  (ADR 0234).
- **A data subject** is a node the binding's classifier names as one. For the
  shape-derived classifier (ADR 0232), that is a focus node of a shape
  annotated `varve:dataSubject true`.
- **A link predicate** of B is a predicate the classifier declares as
  carrying ownership (`varve:subjectLink`, ADR 0232).
- **subjects_p(n)**, for a node n at position p, is the least set such that:
  - it contains n when n is a data subject at p;
  - it contains subjects_p(m) for each quad `(n ℓ m)` of Γ_B at p, with ℓ a
    link predicate of B.

  Ownership is transitive. A space linked to a lease, linked to a tenant,
  belongs to the tenant.
- **A classified node** is a node with a non-empty subjects_p.
- **A triple term**'s subjects are the union of its components' subjects,
  recursively.

### 2. Rows, and what a quad is about

- **The row** of node n in B is every quad of Γ_B whose subject is n.
- **about_p(q)**, for a quad q = `(s p o g)` with g in Γ_B, is the union of:
  - subjects_p(s);
  - subjects_p(o), unless p is a **public link** of B (point 3).
- **A quad in a graph outside every Γ_B** is about no subject. Its graph alone
  governs it (ADR 0107).
- Erasure mode adds the subjects whose key ids the quad's private terms
  carry (part 2, #203).

The object half closes the gap the maintainer found on the plan. Without it,
a visible row that links to a hidden subject (`ex:acme ex:employs
ex:person42`) reveals that subject's IRI. With it, the quad belongs to
person42's row too, and is visible only to a caller who reads both.

### 3. Public links

- **A public link** of B is a predicate whose object reveals nothing about the
  row it names. It is declared by `varve:publicLink true` on a property shape
  (ADR 0232).
- **`rdf:type` is public in every binding.** Its object is a class.
- **A quad under a public link is about its subject's subjects only.** Its
  object's IRI is then visible to every reader of the subject's row, and the
  declaration is what says so.

### 4. Visibility

A caller C reading at position p sees quad q in graph g when both of these
hold:
- g is readable (ADR 0107, unchanged);
- g is in no Γ_B, or about_p(q) ⊆ S_B(C).

**S_B(C), the caller's subject set for B**, is the union of the subject sets
of C's `read` and `write` grants that cover a graph of Γ_B. It is **`all`**
when any such grant carries no subject terms, so **every grant declared
before this milestone means what it meant**. A grant's subject sets are ADR
0234's forms.

- **The empty set ⊆ anything**, so an unclassified quad is governed by its
  graph alone.
- **The subject set is per binding, not per graph.** With ADR 0234's P1, a
  row-restricted caller reads every graph of Γ_B, so every input of a
  classification it can see lies in graphs it reads.
- **An admin** reads every row, as ADR 0107 point 1 has every graph.

### 5. What a hidden row's IRI is

**No response to C carries the IRI or blank node label of a classified node
whose subjects are not all in S_B(C), unless a quad under a public link
names it.** The node's own row is hidden by subjects_p(s). Every quad whose
object is that node is hidden by point 2's object half. A triple term
carrying it is hidden by its components.

### 6. Determinism

**classification_p is a function of G_p ∩ Γ_B and of the binding in force at
p**: the declaration at the management position the commit at p was
authorised under (ADR 0165). It is not a function of history. So a quad's
about-set can change without the quad changing. If `alice a ex:Person` is
retracted, alice's row is unclassified at the next position. ADR 0237 says
how the feed delivers that.

### 7. The property

For generated datasets, classifier bindings, shapes and grants (every
subject-set form of ADR 0234, a caller with an empty subject set, an admin,
and one with only graph-level grants), for every caller and every surface of
ADR 0237:
- **no more:** no response carries an IRI, literal or label that occurs only
  in quads the caller does not see. In particular, it carries no IRI of a
  classified node outside the caller's subject set, unless a public link
  names it (point 5);
- **no less:** each answer equals the same request over the sub-dataset of
  the quads the caller sees, computed from scratch by point 4 over a
  from-scratch classification.

It runs at land-gate depth on pull requests and at full depth in the nightly
`long` job (`docs/testing.md` §8).

## Alternatives considered

- **A quad is about its subject's row only**, the plan's first wording. A
  visible row then reveals the IRIs of the hidden subjects it links to. The
  maintainer found the gap on the plan.
- **Refuse, at apply, a binding that leaves a link to a subject undeclared**,
  the maintainer's second option. Apply can see only the predicates the
  shapes describe, and data can use any predicate, so point 5 could not hold
  for the data itself. A runtime gate refusing such a commit would have its
  outcome depend on whether the object is classified, which a writer with
  graph-level grants on part of Γ_B may not read.
- **Every classified occurrence, wherever it stands**, as ADR 0023's wording
  allows. A predicate's or a literal's classification would then need its
  own assignment, which no use asks for. The object half is the one that
  names a node.
- **Hide the object's IRI and keep the quad**, by rewriting it. A source that
  returns a quad the store does not hold breaks every oracle the grant
  properties use. It is also what erasure's shredded form (part 2, Q4) is
  for.
- **A subject set per graph.** A row-restricted caller could then read part
  of a classification's inputs, and its writes could probe the rest. ADR
  0234's P1 needs one set per binding.
- **Classification per graph, intra-graph.** It is safe against probing, but
  a subject's type in one graph would leave its rows in another
  unclassified, and so visible to every reader of that graph. That is a
  footgun with personal data.

## Consequences

- **A row is no longer all or nothing.** A row's quads that link to another
  subject's row are visible only to readers of both. ADR 0236's write rule
  accounts for it.
- **Links to IRIs outside Γ_B,** such as an external vocabulary, cost a
  row-restricted writer a declaration. It writes them only under a public
  link (ADR 0236 W4). `docs/operator/row-level-access.md` says so.
- **The read filter probes two handles per quad**, the subject and the
  object, plus a decision per predicate (ADR 0235).
- **ADR 0107 point 8** ("a grant cannot name anything but graphs") is closed
  by the amendment. Its ruling `RowLevelIsMilestone9` moves to this set,
  superseded.

## Checks

- **Checked against the accepted ADRs** (0001–0190) and specification 1.8.
  Touches:
  - **0023**: the classifier's assignment of occurrences, made precise. ADR
    0231 records it by amendment;
  - **0107**: amended, as above;
  - **0165**: the binding in force at a position;
  - **0166**: the declarations;
  - **0174**: the data graph of a binding;
  - **0190**: point 7's lesson, applied to rows.

  No conflict.
- **Layer ownership.** The rule is stated over `Varve.Rdf`'s types (ADR
  0235). The classification is `Varve.Classification`'s (ADR 0232). The
  subject sets are `Varve.Management`'s (ADR 0234).
- **Analyzer rule.** None new.
- **Open questions owned.** None.

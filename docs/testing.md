# Testing strategy

What each kind of test in this repository is for, and which pattern to reach
for when adding one. The rules here are enforced where they can be; where a
rule is only written down, it says so.

## 1. The W3C suites are the acceptance gate

A syntax feature is not done until its manifest entries pass or each failure
carries a written, justified exemption. `eng/ratchet.cs` holds the line:
`tests/Varve.Conformance.Tests/baseline/passing.txt` lists what passes today,
and the build fails when one of them stops.

**Guard counts go with every suite.** A manifest that silently stops being read
makes its suite pass by having nothing in it, and "more than zero" does not
catch a suite read as nine cases instead of three hundred and thirteen. Each
wired suite has its case count pinned in `SubmoduleGuardTests`, and a second
test fails when a suite has no count at all.

**The SPARQL syntax suites are a second list, parsed at their own version.**
`SparqlSuite.All` names the fifteen SPARQL 1.0, 1.1 and 1.2 syntax suites,
each with the version its cases are parsed at (`docs/spec/sparql-grammar.md`
§5); `SparqlGuardTests` pins their counts, 554 in all. They are kept apart
from `ConformanceSuite.All` because that list is about streaming document
readers — a format, the chunk-boundary oracle, the pull-reader agreement —
and a SPARQL case is parsed whole. What stands in for the oracle is the
UTF-8 / UTF-16 agreement: every case parsed both ways must give the same
tree or the same error at the same offset. A positive case also passes only
if the serialiser's text parses back to the identical tree, which is the
corpus half of the round-trip property (§3). Same baseline, same ratchet.

## 2. The chunk-boundary oracle — for every streaming reader

**Standing rule: every syntax package runs the chunk-boundary oracle over its
own manifests, and a new one is not done until it does.**

The shape:

> Parse each corpus input whole. Parse it again split into two segments at
> every byte offset. Require the same answer every time — the same quads when
> accepted, the same error kind and the same position when rejected.

Four things make it worth more than the tests you would write by hand.

- **The expected value is computed, not authored.** Whatever the whole-document
  parse said is the answer the split parses must give. So the corpus costs
  nothing to add to: point it at a manifest and it covers every file in it,
  with no one reading them.
- **It is an oracle, not a conformance test.** It never asks whether the answer
  agrees with the specification — the suites do that. It asks only whether the
  parser agrees with itself, which is a different defect class and one the
  suites cannot see, because they parse whole documents.
- **Negative cases count.** A parser that rejects the right documents in the
  wrong place, or for the wrong reason, is reporting something a user will act
  on. The position is the part most likely to go quietly wrong, because a
  buffer-relative computation gets it right on a whole document and wrong on a
  fragment.
- **It finds a defect class, not a defect.** Turtle's reader had six of them,
  none reached by hand-written tests: a keyword decided on too few bytes, a
  number or language tag ending where the buffer did, a multi-byte character
  cut in half, an escape near the end mistaken for truncation, a comment with
  no newline consumed as finished, and a blank node counter not rewound. Every
  one is "the split changed the answer".

**The rule it enforces in the reader.** A token whose end is settled by the
byte *after* it must not be decided at the end of a buffer that can still grow.
The scanner is told whether its buffer is the document's last, waits when it is
not, and decides when it is (`docs/spec/turtle.md` §8).

**A line-based reader may not need the mechanism, and still runs the oracle.**
N-Triples dispatches a line only once its newline has been found, so its parser
never sees a fragment and no token can be decided early. That is an argument;
the oracle is the measurement, and it is what turns "should be fine" into 213
inputs that agree.

**Where it lives.** `tests/Varve.Conformance.Tests/ChunkBoundaryOracleTests.cs`,
over `ConformanceSuite.OracleCorpus` — the ratcheted suites plus those whose
inputs exist but whose results are not claimed yet. Kept apart from the
ratchet's corpus so that widening the oracle can never widen the baseline.
`EveryFormatIsCoveredByTheOracle` fails when a format is added without one.

## 3. Round trips are properties, not examples

Where a reader and a writer are inverses, say so as a property and let the
corpus supply the cases.

- **N-Triples canonical form is byte-stable**: parse a canonical document,
  write it, get the same bytes.
- **Turtle and TriG writing is a fixed point**: write, read back, write again,
  get the same bytes. Weaker than byte-stability on purpose — blank node labels
  are the parser's, so the first write may rename (`docs/spec/turtle.md` §4) —
  and still strong enough to catch a scheme that renames on every pass, which
  is what a pipeline of stages would pay for repeatedly.

Both run over every manifest input as well as over generated documents. The
generator exists to reach the cases the corpus does not: for the fixed point
that is a document mixing named and anonymous blank nodes, so that the renaming
path is exercised rather than avoided.

- **The SPARQL algebra round-trips exactly**: write a tree, parse the text,
  get the identical tree, by record equality (`docs/spec/sparql-algebra.md`
  §6). Stronger than a fixed point, because the tree is the meaning and the
  text is canonical. Held over generated trees inside the parser's image and
  over every positive corpus case.

## 4. Allocation is measured as a difference

Zero bytes per quad is asserted by parsing two documents of different sizes and
subtracting, not by a single absolute number. An absolute number measures the
harness as much as the parser; the difference cancels it.

The measurement runs on the fragmented paths too, with segment sizes small
enough to cut constructs in half, because a buffer copied per chunk is a cost
the whole-span path never shows.

**Every reading goes through `tests/AllocationMeter.cs`**, linked into each
test project that measures (issue #32). Two things outside the code under
test move a single reading of `GC.GetAllocatedBytesForCurrentThread`. A
collection in the window — started by any thread — retires the thread's
allocation context and the counter keeps its unused tail, up to one 8 KB
quantum, so a reading is taken inside a no-GC region and discarded if the
region broke. Tiered compilation can stack-allocate an object the caller
discards once the call is inlined, so the measured action returns what it
made and the meter keeps it alive; and a pair of readings counts only when
the next round reproduces it, so a tier-up between the two sides of one round
is not used. There is no warm-up count to tune: the rounds are the warm-up.

## 5. Fitness tests for the things a review forgets

Layer direction and layer declaration are analyzers (`DD0001`, `VARVE0005`).
The dependency register, the native-asset ban and the package metadata are
file-based apps under `eng/`, run in CI. Each of them replaces a rule someone
would otherwise have to remember.

**Prove a gate's failure path by running it.** A gate that has never failed is
a gate nobody has tested. `eng/native-assets.cs` is pointed at a real restore
graph that contains a native asset and at one that does not, and both outcomes
are asserted.

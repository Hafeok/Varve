---
set: bool-values
namespace: varve
origin: "DD0016 findings on Varve.Xsd and Varve.Rdf in session 2 of #43"
decisions:
  - key: BoolParameterIsTheValue
    statement: "A model member whose bool parameter is the boolean value being represented, XsdBoolean's constructor and InlineValue.FromBoolean, takes a bool, because an enum of two members would be a second name for the value space"
---

# A `bool` that is the value

**Unaccepted.** Filed by session 2 of #43, for the maintainer.

`DD0016` (tier 2) warns on a `bool` parameter on a model member, because a call
site reading `Find(id, true, false)` cannot say which flag is which. Its rule
page names the case where it is wrong: "a `bool` that is genuinely the data",
where "the enum would be ceremony". There the page's answer is a
`[DesignDecision]` citing the decision that says the `bool` is right.

Two members are that case. `XsdBoolean(bool value)` constructs `xsd:boolean`
from its value space, which is `{true, false}`. `InlineValue.FromBoolean(bool value)`
makes an inline `xsd:boolean`. The call sites read `new XsdBoolean(x)` and
`InlineValue.FromBoolean(x)`, and a two-member enum would be a second name for
`bool`.

The package could exempt a wrapper's own constructor from `DD0016`, as `DD0013`
exempts a wrapper's own primitive. That is proposed upstream as
decision-driven-analyzers#60. Until then, this is the documented path.

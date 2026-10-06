# DCO fixture

The failure path of `eng/dco.cs`: a gate that has never failed is a gate
nobody has tested (`docs/testing.md` §5). Commits are read from a file, not
from git, so the cases are exact and need no repository. The identity map is
this directory's own: Alice, with Bob as her delegate, and the agent Helper,
for whom Alice is responsible.

```
dotnet run eng/dco.cs -- --commits tests/fixtures/dco/failing.txt --identities tests/fixtures/dco/identities.json
dotnet run eng/dco.cs -- --commits tests/fixtures/dco/passing.txt --identities tests/fixtures/dco/identities.json
```

The first exits 1 and names five commits, the second exits 0. The `dco-fixture`
job in `eng/ci.cs` runs both and requires exactly that.

`failing.txt`:

1. a human's commit with no sign-off;
2. signed off with another of the author's own emails, which is not the
   author's identity on that commit;
3. signed off by a different human;
4. an agent's commit signed off by the agent;
5. an agent's commit signed off by a human who is neither its responsible human
   nor a delegate.

`passing.txt`: the author's own sign-off (email case ignored); an agent signed
off by its responsible human, with any of that human's emails; an agent signed
off by a delegate; exempt automation; a merge; and an author the map does not
know, signed off by themself.

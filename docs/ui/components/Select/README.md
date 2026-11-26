# Select

A labelled native `<select>` in `Field`'s form. Parameters: Id, Label, Options (value, text), Selected, Help, Disabled. Generic: the regimes are `RegimeSelect`'s (a Part), tested in bUnit.

- **Parts over it:** `RegimeSelect` offers only the regimes the service description lists; `AsOfControl` composes it with `Field` and `Button`. Their behaviour is bUnit tests on the parts.

Level: atom. Cases: `default`, `help`, `disabled`.

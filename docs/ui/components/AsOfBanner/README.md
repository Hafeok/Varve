# AsOfBanner

Shown when a view is pinned to a historical position with `Varve-As-Of`. The banded left edge is the varve: the one place the record motif appears in the interface.

- **Consumer provides:** the as-of position, the commit time at that position, the current head, and a "Return to head" action.
- It sits directly below the `ViewHead`, above everything else, and stays while the pin holds. Every resource in the view is read at that position; if one cannot be, that resource shows its problem instead of current data.
- Uses `past` on `past-soft`, a tone used for nothing else.

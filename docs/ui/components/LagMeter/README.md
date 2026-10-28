# LagMeter

How far a follower, a projection or a binding is behind its head, against the bound the server judges readiness by (`Varve:Health:ReadyLag`, or the binding's own).

- **Consumer provides:** the lag in positions, the bound, and where the bound comes from. The meter is a native `<meter>` with `max` at twice the bound, `low` at half the bound, `high` at the bound and `optimum` 0, so the browser picks the tone and assistive technology reads the value.
- Always pair it with the number: the meter is a glance, the figure is the fact.
- A lag of 0 still renders the meter (empty), so rows line up.

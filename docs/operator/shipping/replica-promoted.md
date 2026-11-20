# `replica-promoted`

| | |
|---|---|
| **Where** | `/health/ready` and `varve status`, under a dataset's `shipping` member |
| **The shipper** | has stopped for this target, and wrote nothing after the promotion |
| **The dataset** | is not affected: the source still commits |

An operator promoted the copy (ADR 0185). The shipper reads the copy's
lineage record every round, and stops when it says `promoted`. `detail`
gives the position it was promoted at.

From that position on there are two datasets. The promoted copy is written
by the fleet that serves it. The source is still written by this one.
Commits the source makes after the promoted position are on no copy.

## What to do

- If the promotion was meant, the source is the old primary. Stop writing
  to it: point clients at the promoted copy, and declare the source closed
  when nothing reads it. Its commits past the promoted position were never
  shipped; `varve status` on the source showed that lag before the
  promotion.
- To make the old primary a copy of the new one, ship from the new one to
  a fresh prefix. A promoted copy takes no write from its old source.
- If the promotion was a mistake, it cannot be undone: the copy has
  claimed an epoch and may have committed. Decide which dataset is kept,
  as for [`shipping-diverged`](shipping-diverged.md).
- Remove the target from the source's declaration when the decision is
  made. The shipper does not start again for it by itself.

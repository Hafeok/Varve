# ViewHead

The top of every screen: the resource's name as `page-title`, a crumb above it, and the position stamps the view was read at, right-aligned.

- **Consumer provides:** the title, the crumb (the parent resource, linked), and one `PositionStamp` per position the view's data came from: a data-plane `Varve-Position`, a management position, or both.
- Every screen has one. A screen with no stamp is a defect: it hides which layer of the record the operator is looking at.
- When the view is pinned as of a position, an `AsOfBanner` comes directly below the view head.

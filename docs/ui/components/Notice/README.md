# Notice

A sentence the operator needs before acting, inline in the flow: the drift note before a declaration-changing act, a follower's read-only note, an unreachable server.

- **Consumer provides:** a one-line title and a short body. `vw-notice--warn` when the operator should think before going on; the default (neutral) for plain information.
- Not for errors from the API: those are `ProblemPanel`s.
- Generic: the molecule carries no wording of its own, because lower levels do not know Varve's domain. The page passes the words in.
- The drift note is one `Notice` (warn) whose text lives once at the page level, passed into `ConfirmDialog`'s note slot. Its text is fixed: title *The declaration will drift from Git*, body *This changes the dataset's declaration on the server. Your declaration files in Git will differ until you update them; `varve apply --plan` will show the difference.*

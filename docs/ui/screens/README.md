# Console screens

Reference screens for Web UI part 1 (#167) and the data explorer (#96): one page per screen and state, built only from the markup of the contract cases under `../components/*/cases/` over `../tokens.css` and `../bundle.css`. Open a page from a checkout; it renders as the app does, in the light theme.

They are the picture of what the pages compose, not a second contract: the cases pin each component's markup, and the pages under `src/Varve.WebUi/Pages` decide what is composed. When a page and a screen disagree, the page is checked against the cases, and the screen is regenerated.

`screens.css` holds the one thing a static page cannot do: show an opened `ConfirmDialog` over its backdrop, as `showModal()` does.

| Screen | States |
|---|---|
| Datasets | [Ready](datasets--ready.html), [Loading](datasets--loading.html), [Empty](datasets--empty.html), [403](datasets--forbidden.html), [Unreachable](datasets--unreachable.html), [Anonymous](datasets--anonymous.html) |
| Dataset: notes | [Writer](dataset--ready.html), [As of](dataset--asof.html), [Checkpoint: confirm](dataset--checkpoint-confirm.html), [Checkpoint taken](dataset--checkpoint.html), [Close dialog](dataset--closing.html), [Close dialog: Memory](dataset--closing-memory.html), [Close: 412](dataset--conflict.html), [Closed](dataset--closed.html), [Follower](dataset--follower.html), [Failed](dataset--failed.html), [403](dataset--forbidden.html) |
| Health | [Not ready](health--notready.html), [Ready](health--ready.html), [Superseded](health--superseded.html), [Draining](health--draining.html), [Loading](health--loading.html), [Unreachable](health--unreachable.html) |
| Fleet | [System writer](fleet--writer.html), [System follower](fleet--follower.html), [403](fleet--forbidden.html) |
| Commits | [Live](commits--live.html), [A commit's delta](commits--delta.html), [Paused](commits--paused.html), [Empty](commits--empty.html) |
| Grants | [Table](declarations--grants.html), [Source](declarations--source.html), [As of](declarations--asof.html), [403](declarations--forbidden.html) |
| Query: notes | [SELECT](query--select.html), [CONSTRUCT](query--construct.html), [ASK](query--ask.html), [Syntax error](query--error.html), [Stopped at the ceiling](query--stopped.html), [Streaming](query--streaming.html), [Cut](query--cut.html), [403](query--forbidden.html), [As of a position](query--asof.html): the explorer's first screen (#355). These were captured from the running page against a real server, so their positions are small. Streaming, Cut and 403 are the select page with `ResultsTable`'s and `ProblemPanel`'s cases in place of the answer. Its two rows of `vw-actions` wait on a form row (#383) |
| Sign-in | [Not configured](signin--notconfigured.html): the server has OIDC and no client for the Web UI (ADR 0245); it replaces every screen |

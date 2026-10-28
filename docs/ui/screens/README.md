# Console screens

Reference screens for Web UI part 1 (#167): one page per screen and state, built only from the markup of the contract cases under `../components/*/cases/` over `../tokens.css` and `../bundle.css`. Open a page from a checkout; it renders as the app does, in the light theme.

They are the picture of what the pages compose, not a second contract: the cases pin each component's markup, and the pages under `src/Varve.WebUi/Pages` decide what is composed. When a page and a screen disagree, the page is checked against the cases, and the screen is regenerated.

`screens.css` holds the one thing a static page cannot do: show an opened `ConfirmDialog` over its backdrop, as `showModal()` does.

| Screen | States |
|---|---|
| Datasets | [Ready](datasets--ready.html), [Loading](datasets--loading.html), [Empty](datasets--empty.html), [403](datasets--forbidden.html), [Unreachable](datasets--unreachable.html), [Anonymous](datasets--anonymous.html) |
| Dataset: notes | [Writer](dataset--ready.html), [As of](dataset--asof.html), [Checkpoint taken](dataset--checkpoint.html), [Close dialog](dataset--closing.html), [Close: 412](dataset--conflict.html), [Follower](dataset--follower.html), [Failed](dataset--failed.html), [403](dataset--forbidden.html) |
| Health | [Not ready](health--notready.html), [Ready](health--ready.html), [Superseded](health--superseded.html), [Draining](health--draining.html), [Loading](health--loading.html), [Unreachable](health--unreachable.html) |
| Fleet | [System writer](fleet--writer.html), [System follower](fleet--follower.html), [403](fleet--forbidden.html) |
| Commits | [Live](commits--live.html), [A commit's delta](commits--delta.html), [Paused](commits--paused.html), [Empty](commits--empty.html) |
| Declarations | [Grants](declarations--grants.html), [Source](declarations--source.html), [As of](declarations--asof.html), [403](declarations--forbidden.html) |

# Shell

The frame around every screen: the bar (core mark, server address, one sign-in indicator per plane), the navigation rail, and the main column.

- **Consumer provides:** the server's base address, each plane's sign-in state, the navigation items with the current one marked `aria-current="page"`, and the screen in `vw-shell__main`.
- **A skip link comes first.** "Skip to main content", `vw-skip`, is the first focusable element and targets `#main`; it is hidden until it has focus. The navigation repeats on every screen, so WCAG 2.4.1 needs it. The main column is `<main id="main" tabindex="-1">`, so it takes focus without a ring.
- **The navigation is named "Main"** (`aria-label="Main"`), which a screen reader announces as "Main navigation": the operator's word, not the project's name.
- **Planes are shown separately.** The console holds two tokens; each `vw-plane` says whether its plane is signed in. A plane without a token shows `vw-plane--absent` and its word, never a merged "signed in".
- **Navigation is words.** Part 1's groups: *Operate* (Datasets, Health, Fleet), *History* (Commits), *Declarations* (Grants, Agents and roles, Graph templates, Shapes). Part 2 adds an *Explore* group to the same rail.
- In anonymous mode both indicators read "anonymous" with the **warn** state badge: every caller holds every permission, which is worth attention on any server but a laptop (review F25).
- **Sign out** is the bar's one action when signed in: a small quiet `Button` passed in the `BarActions` slot, after the planes. Anonymous mode and a signed-out tab have none.
- Below 720px the rail becomes a wrapping row above the main column.

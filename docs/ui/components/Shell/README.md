# Shell

The frame around every screen: the bar (core mark, server address, one sign-in indicator per plane), the navigation rail, and the main column.

- **Consumer provides:** the server's base address, each plane's sign-in state, the navigation items with the current one marked `aria-current="page"`, and the screen in `vw-shell__main`.
- **Planes are shown separately.** The console holds two tokens; each `vw-plane` says whether its plane is signed in. A plane without a token shows `vw-plane--absent` and its word, never a merged "signed in".
- **Navigation is words.** Part 1's groups: *Operate* (Datasets, Health, Fleet), *History* (Commits), *Declarations* (Grants, Agents and roles, Graph templates, Shapes). Part 2 adds an *Explore* group to the same rail.
- In anonymous mode both indicators read "Anonymous" with the neutral state badge.
- Below 720px the rail becomes a wrapping row above the main column.

# Shell

The frame around every screen: the bar (core mark, server address and the instance that answered, one sign-in indicator per plane), the navigation rail, and the main column.

- **Consumer provides:** the server's base address, the instance that answered, each plane's sign-in state and who it names, the navigation items with the current one marked `aria-current="page"`, the datasets the selector offers with the one selected and its own screens, and the screen in `vw-shell__main`.
- **The instance that answered** follows the server's address (`vw-shell__instance`, review F01): its name and its role in the system dataset, "varve-1 · system writer", read from `/manage/system`. Until the server names the instance (#193), the role alone: "system writer". Behind one address each read may reach another instance, so it is read again on every navigation.
- **Who is signed in** (review F02): the agent the tokens name, `iss#sub`, as the server and the grants name it.
  - **One agent:** when both planes name the same one, which is usual, "Signed in as" it is said once (`vw-shell__who`), before the planes' state chips, and its roles tooltip lists each plane's roles.
  - **Two agents:** only when the planes name different ones, each plane names its own, with its own roles.
  - **Long IRIs:** an IRI over 40 characters is truncated in the middle (`vw-shell__agent`), so the bar stays on one line at 1280px; the full IRI is its `title` and its accessible name.
  - **Roles:** focusing the agent, or hovering, shows the roles (`vw-shell__roles`, a tooltip the agent is described by). Escape hides it without moving focus or the pointer (WCAG 1.4.13).
- **A skip link comes first.** "Skip to main content", `vw-skip`, is the first focusable element and targets `#main`; it is hidden until it has focus. The navigation repeats on every screen, so WCAG 2.4.1 needs it. The main column is `<main id="main" tabindex="-1">`, so it takes focus without a ring.
- **The navigation is named "Main"** (`aria-label="Main"`), which a screen reader announces as "Main navigation": the operator's word, not the project's name.
- **Planes are shown separately.** The console holds two tokens; each `vw-plane` says whether its plane is signed in. A plane without a token shows `vw-plane--absent` and its word, never a merged "signed in".
- **Navigation is words.** Part 1's groups: *Operate* (Datasets, Health, Fleet), then a **dataset selector** (review F22) with the selected dataset's own screens under it: *History* (Commits) and *Declarations* (Grants, Agents and roles, Graph templates, Shapes). The selector defaults to the dataset open, else the dataset last viewed, else the first; choosing another on a dataset's screen opens the same screen of that dataset. Only screens that are built are listed. Part 2 adds an *Explore* group to the same rail.
- In anonymous mode both indicators read "anonymous" with the **warn** state badge: every caller holds every permission, which is worth attention on any server but a laptop (review F25).
- **Sign out** is the bar's one action when signed in: a small quiet `Button` passed in the `BarActions` slot, after the planes. Anonymous mode and a signed-out tab have none.
- Below 720px the rail becomes a wrapping row above the main column.

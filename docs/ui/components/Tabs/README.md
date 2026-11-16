# Tabs

Two or more views of one screen, such as a dataset's grants as a table and as the declaration's source (review F23). Each view has its own address, so a view can be linked, reloaded and reached with the Back button.

- **Consumer provides:** a label naming what the views are of ("Grants"), and each view's word, address and whether it is the one shown, marked `aria-current="page"`.
- **Links, not an ARIA tab list.** Each tab is a link to the view's address, in a `nav` named by the label: no script decides what shows, and the screen reads its view from its address.
- Sits directly below the view head and any notice under it, above the view.
- A molecule: words and links, no other component.

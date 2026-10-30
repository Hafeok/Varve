# Tail

The live tail's indicator, at the head of a `CommitList`: whether new commits are arriving on screen, or held back while the operator reads.

- **Consumer provides:** whether the tail is paused, while paused how many commits have arrived and are not shown yet, and what showing them does.
- **Live** is a pulsing `ok` dot and the word "Live". **Paused** is a hollow muted dot and says so: "Paused while you read". With commits waiting it offers them as a button, "Show 3 new", which shows them and resumes the tail (review F20).
- **It is a polite live region** (`aria-live="polite"`), so a change of state and the count waiting are announced (review F27). The words carry the state, so it reads the same in forced colours and with motion reduced (`prefers-reduced-motion` stops the pulse).
- The tail pauses when the operator scrolls away from the top of the list, and resumes at the top; the page decides when, and the atom only shows it.
- An atom: a word and a dot, no other component.

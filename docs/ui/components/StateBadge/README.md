# StateBadge

A state from the API as a word with a mark. Five tones, each with its own mark shape: ok (disc), warn (triangle), critical (square), neutral (ring), past (bands).

- **Consumer provides:** the API's word, verbatim, and its tone. The atom knows nothing about datasets: the tone comes from the state-to-tone mapping (README, Levels), never from a lookup inside the badge. A new state maps onto one of the five tones, never a sixth.
- The word is the content. The mark is decoration (`::before`) and the colour is the third channel.
- Dataset rows usually show two badges: the dataset's state (`open`, `closed`, `failed`) and this instance's role (`writer`, `follower`, `elsewhere`).

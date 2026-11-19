# The graph view's spike

Not a test project and never shipped: the page and script the graph view's
spike (#355, ruling 4 of 2026-11-02) builds into the Web UI, only when it is
published with `-p:VarveGraphSpike=true`. `run-spike.js` serves that publish
and reads the page's timings at 50, 100, 150, 200 and 300 nodes in Chromium.

# Fixture: a page with a script

Its links are whole, so the link gate passes it, and the build refuses it:
the site ships no script (ADR 0247 point 6).

<script>document.title = "ran";</script>

# releases/

One file per release: `<version>.yaml`, the release descriptor. Adding one in a
pull request proposes the release, and landing that pull request at its
approved head cuts it. The format and the rules are in
[`docs/releases.md`](../docs/releases.md), and the reasons in
[ADR 0102](../docs/adr/0102-a-release-is-a-descriptor.md).

`v0.1.0-preview.1.yaml` records the one release cut before descriptors
existed (ADR 0085): pinned to its tagged commit, so it is never cut again, with
its `CHANGELOG.md` section as the summary. `CHANGELOG.md` is the projection of
this directory.

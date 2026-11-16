---
set: the-web-ui-in-the-container-image
namespace: varve
adr: 0245
decisions:
  - key: ZipAssetNoImage
    statement: "The Web UI ships as a release zip with its own SBOM and attestations, and the same bundle, unpacked unchanged from the zip the same run attested, is in the server's container image; there is no static image of its own"
  - key: BundleInTheImageAtAFixedPath
    statement: "The image carries the Web UI bundle at /app/webui, read-only, built once from the same commit and run as the server and taken by both platforms"
  - key: ServedByDefaultInTheImage
    statement: "The image sets Varve:WebUi:Path to its bundle and Varve:WebUi:Settings to Generated, so serve answers /ui/ with nothing else set; outside the image an unset path still means no /ui/"
  - key: WebUiEnabledIsTheOffSwitch
    statement: "Varve:WebUi:Enabled, true by default, turns the Web UI off: false adds no /ui/ at all, whatever the path"
  - key: SettingsFileOrGenerated
    statement: "Varve:WebUi:Settings is File by default, serving the bundle's appsettings.json as edited, or Generated, answering it from the server's configuration and never serving the bundle's file or its .br and .gz copies; nothing is detected from the bundle"
  - key: GeneratedSettingsAreAnAllowList
    statement: "The generated settings are written field by field from a closed record: Server, and the mode, the server's own authority and role claim, and the Web UI's public client from Varve:WebUi:Auth; no configuration section is copied and nothing secret is ever in them"
  - key: NotConfiguredIsNeverAnonymous
    statement: "A server with OIDC and no Web UI client answers Mode NotConfigured with the settings to add, and the console, for NotConfigured or any mode but Oidc and Anonymous, says sign-in is not configured, reads nothing, and never shows anonymous"
  - key: WebUiSettingsReadOnlyWhenGenerated
    statement: "Varve:WebUi:Server or any Varve:WebUi:Auth setting while Settings is File, a half-set client, an unknown grant, or a client on an anonymous server refuses the start, naming the setting"
  - key: ImageSbomContainsTheBundle
    statement: "The image's SBOM contains the Web UI bundle's document once, its workload packs included, and a proposed release whose image SBOM lacks a package of the zip's is red"
---

The rulings of [ADR 0245](../adr/0245-the-web-ui-in-the-container-image.md),
filed unaccepted by the session for #296 (ADR 0066). Every citation is
`CS0618` until the maintainer accepts them, or his delegate for a decision he
has read (ADR 0239).

Moved here, superseded with its key: `ZipAssetNoImage` (0202, point 6), whose
"in no container image" this ADR reverses.

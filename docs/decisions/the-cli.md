---
set: the-cli
namespace: varve
adr: 0104
decisions:
  - key: OneExecutableIsServerAndCli
    statement: "Varve.Server is the tool varve: one executable, one composition root, one AOT publish; varve serve and a bare varve run the server of ADR 0101 unchanged"
  - key: CliCommands
    statement: "The commands are create, info, load, query, update, export, checkpoint, feed and serve, each taking a dataset directory or a dataset URL where both make sense; load is embedded only"
  - key: EmbeddedModeOpensDirectoryWithoutAuth
    statement: "Embedded mode opens the directory with FileStorage directly and no authentication; the file permissions and the lease are the boundary, and SERVICE and LOAD take their policy from --allow-endpoint and --allow-source, empty by default"
  - key: RemoteCliUsesBearerTokens
    statement: "The CLI talking to a remote server uses bearer tokens from the device code or client credentials flow of ADR 0037 and stores nothing but the issuer's refresh token and what identifies its issuer, in the credential file of this ADR"
  - key: CredentialFileSupersedesPlatformStore
    statement: "The refresh token is kept in one file under the user's profile, mode 0600 on Unix and refused when wider, DPAPI-protected on Windows through System.Security.Cryptography.ProtectedData, and weaker than the macOS Keychain, which the operator guide says"
  - key: NoStoreFlag
    statement: "--no-store obtains a token and writes nothing to disk, the mode for CI and shared machines"
  - key: SystemCommandLineInTheHost
    statement: "System.CommandLine 2.0.x parses the command line, registered in Varve.Server alone, on the condition that the AOT publish stays green"
  - key: ToolPackageFrameworkDependentAotIsTheGate
    statement: "The tool package is framework-dependent with ToolCommandName varve, and the Native AOT single-file binary published by the native aot job on both runners, with the CLI smoke run against it, is the gate"
  - key: SecondExecutableRevisitCondition
    statement: "A second executable moves the host wiring both would share into a layer-5 Varve.Hosting package and supersedes the one-executable ruling"
---

The rulings of [ADR 0104](../adr/0104-the-cli.md), filed unaccepted by milestone 7b of #11
(ADR 0066). Every citation is `CS0618` until the maintainer accepts them.

`RemoteCliUsesBearerTokens` moved here from ADR 0037's set: ADR 0104 supersedes point 8's
storage clause in part, and the ruling keeps its key with the new clause and this ADR's
acceptance.

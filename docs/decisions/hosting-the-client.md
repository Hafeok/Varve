---
set: hosting-the-client
namespace: varve
adr: 0131
decisions:
  - key: AddVarveInTheClient
    statement: "AddVarve on IServiceCollection lives in Varve.Protocol.Client and registers the client and, with a setup, a hosted service whose failed apply fails startup with the problem"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: ExtensionsAbstractionsAdmitted
    statement: "Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Hosting.Abstractions are admitted for Varve.Protocol.Client"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: VarveSetupApplyCommand
    statement: "varve setup apply reads a setup file and calls the same ApplyAsync, exiting 1 on a refusal and 2 on a file outside the subset"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: VarveApplyIsNotSetup
    statement: "varve apply is neither a command nor an alias of setup apply: the Management plane (#105) takes the name for TriG desired-state convergence"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
  - key: AspireRunsVarveSetupApply
    statement: "Varve.Aspire's WithDataset setup runs varve setup apply from the server's image once the server is healthy"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-15T00:00:00Z
---

The rulings of [ADR 0131](../adr/0131-hosting-the-client.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.

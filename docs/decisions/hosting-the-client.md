---
set: hosting-the-client
namespace: varve
adr: 0131
decisions:
  - key: AddVarveInTheClient
    statement: "AddVarve on IServiceCollection lives in Varve.Protocol.Client and registers the client and, with a setup, a hosted service whose failed apply fails startup with the problem"
  - key: ExtensionsAbstractionsAdmitted
    statement: "Microsoft.Extensions.DependencyInjection.Abstractions and Microsoft.Extensions.Hosting.Abstractions are admitted for Varve.Protocol.Client"
  - key: VarveApplyCommand
    statement: "varve apply reads a setup file and calls the same ApplyAsync, exiting 1 on a refusal and 2 on a file outside the subset"
  - key: AspireRunsVarveApply
    statement: "Varve.Aspire's WithDataset setup runs varve apply from the server's image once the server is healthy"
---

The rulings of [ADR 0131](../adr/0131-hosting-the-client.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by milestone Client and integration of #93
(ADR 0066); every citation is `CS0618` until the maintainer accepts them.

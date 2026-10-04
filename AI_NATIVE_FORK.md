# AI-Native Fork Policy

This fork is the server-foundation candidate for the AI-Native Unity Framework.

## Baseline and runtime

- Upstream baseline: `qq362946/Fantasy@493d5d4dd1dd009cdfcd2846b88ebab9746d4504`.
- Supported server and tooling runtime: .NET 10 (`net10.0`) with the repository-pinned SDK.
- `Fantasy.SourceGenerator` remains `netstandard2.0` because it is a compiler component rather than a deployed runtime.

## Supported composition

The supported validation matrix contains `Fantasy.sln`, `examples/Server/Server.sln`, the server templates under `Skills/fantasy-net/templates`, and the tracked Fantasy-Net package. The orphan `Fantasy.Benchmark` project and historical Console examples are not supported and are excluded from the matrix.

Fork changes must remain focused and reviewable. Product gameplay does not belong in this repository; it stays in the parent framework and accesses Fantasy through adapters. Upstream updates require build, package/config regression, startup, dependency-audit, protocol, replay, and load evidence appropriate to the change.

The repository license includes an entity-specific restriction. Legal review remains required before commercial distribution or publication of derived packages.

## 2026-10-04: bounded socket buffer initialization

Fantasy-Net 2026.1.1004-ainative.1 and Fantasy.Unity 2026.1.1002-ainative.1 replace the synchronous receive/send linear probing loops with a direct target and bounded binary fallback (at most 32 writes per buffer). The default Windows requested maximum is retained; OS clamping/scaling ends probing immediately. Public signatures and defaults are unchanged. Nonpositive attempts and a zero step are no-ops; a negative step with positive attempts now throws ArgumentOutOfRangeException. No wire, MTU, connection-lifecycle or gameplay change is included.

The mirrored helpers are checked for source equality. Formal tests cover option limits, clamping/scaling, overflow, failure, disposal, parameters, deepest fallback paths, 10,000 seeded model cases and real socket options. Full tooling/server builds and all 21 package/config/socket tests passed on Windows with .NET 10. Additional platform/load qualification belongs to the parent framework evidence ledger; this is not a Linux/mobile performance claim. Retain the previous commit and tracked package for rollback. License text and distribution restrictions are unchanged.

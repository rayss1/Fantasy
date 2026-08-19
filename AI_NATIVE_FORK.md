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

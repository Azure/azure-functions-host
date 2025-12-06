# .NET 10 Upgrade for Azure Functions Host (WebJobs.Script solution)

## Overview

This scenario upgrades all solution projects to .NET 10 RTM using a Bottom-Up Strategy, strictly following dependency tiers. The goal is to update frameworks, replace deprecated/incompatible packages, and ensure all builds and tests pass. Each tier is upgraded and validated before proceeding to the next, minimizing risk and ensuring stability.

**Progress**: 0/7 tasks complete (0%) ![0%](https://progress-bar.xyz/0)

## Tasks

### [▶] TASK-001: Validate prerequisites
**References**: Plan §Phase 0

- [▶] (1) Ensure .NET 10 SDK is installed and available
- [▶] (2) Validate global.json compatibility if present
- [▶] (3) .NET 10 SDK is available and compatible (**Verify**)

### [ ] TASK-002: Upgrade Tier 1 - WebJobs.Script
**References**: Plan §Tier 1, Plan §4. Project-by-Project Migration Plans

- [ ] (1) Update `TargetFramework` in `src/WebJobs.Script/WebJobs.Script.csproj` to net10.0
- [ ] (2) Update `Microsoft.Extensions.Http.Polly` to 10.0.0; replace/remove deprecated/incompatible packages per Plan §Tier 1
- [ ] (3) Refactor code to remove usages of `Mono.Posix.NETStandard` and `WebApiCompatShim`, replacing with supported .NET APIs
- [ ] (4) Restore dependencies and build project
- [ ] (5) Project builds with 0 errors/warnings (**Verify**)
- [ ] (6) Commit changes with message: "chore(net10): Tier 1 WebJobs.Script upgrade"

### [ ] TASK-003: Upgrade Tier 2 - WebJobs.Script.Grpc
**References**: Plan §Tier 2, Plan §4. Project-by-Project Migration Plans

- [ ] (1) Update `TargetFramework` in `src/WebJobs.Script.Grpc/WebJobs.Script.Grpc.csproj` to net10.0
- [ ] (2) Validate startup and DI patterns for .NET 10 compatibility
- [ ] (3) Restore dependencies and build project
- [ ] (4) Project builds with 0 errors/warnings (**Verify**)
- [ ] (5) Commit changes with message: "chore(net10): Tier 2 WebJobs.Script.Grpc upgrade"

### [ ] TASK-004: Upgrade Tier 3 - WebJobs.Script.WebHost
**References**: Plan §Tier 3, Plan §4. Project-by-Project Migration Plans

- [ ] (1) Update `TargetFramework` in `src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj` to net10.0
- [ ] (2) Update packages: `Microsoft.AspNetCore.Authentication.JwtBearer` to 10.0.0, `Microsoft.AspNetCore.Mvc.NewtonsoftJson` to 10.0.0, remove/replace deprecated packages per Plan §Tier 3
- [ ] (3) Refactor code for ASP.NET Core 10 hosting/auth changes, storage SDK migration, and other breaking changes
- [ ] (4) Restore dependencies and build project
- [ ] (5) Project builds with 0 errors/warnings; app starts and responds on health endpoints (**Verify**)
- [ ] (6) Commit changes with message: "chore(net10): Tier 3 WebJobs.Script.WebHost upgrade"

### [ ] TASK-005: Upgrade Tier 4 - Test Projects
**References**: Plan §Tier 4, Plan §4. Project-by-Project Migration Plans

- [ ] (1) Update `TargetFramework` in all Tier 4 test projects to net10.0:
      - `test/WebJobs.Script.Tests.Shared/WebJobs.Script.Tests.Shared.csproj`
      - `test/WebJobs.Script.Tests/WebJobs.Script.Tests.csproj`
      - `test/WebJobs.Script.Tests.Integration/WebJobs.Script.Tests.Integration.csproj`
- [ ] (2) Update/replace deprecated/incompatible packages per Plan §Tier 4 (e.g., `Microsoft.Azure.Storage.Blob`, `Microsoft.AspNet.WebApi.Core`, `DocumentDB.Core`)
- [ ] (3) Refactor integration tests to use ASP.NET Core test infrastructure and Cosmos SDK as needed
- [ ] (4) Restore dependencies and build all test projects
- [ ] (5) All test projects build with 0 errors/warnings (**Verify**)
- [ ] (6) Commit changes with message: "chore(net10): Tier 4 test projects upgrade"

### [ ] TASK-006: Run and fix all tests
**References**: Plan §6. Testing and Validation Strategy, Plan §Tier 4

- [ ] (1) Run all unit and integration tests in upgraded test projects
- [ ] (2) Fix any test failures related to upgrade (reference Plan §Breaking Changes for common issues)
- [ ] (3) Re-run tests after fixes
- [ ] (4) All tests pass with 0 failures (**Verify**)
- [ ] (5) Commit test fixes with message: "chore(net10): Fix tests after upgrade"

### [ ] TASK-007: Upgrade Tier 5 - Benchmarks
**References**: Plan §Tier 5, Plan §4. Project-by-Project Migration Plans

- [ ] (1) Update `TargetFramework` in `perf/WebJobs.Script.Benchmarks/Microsoft.Azure.WebJobs.Script.Benchmarks.csproj` to net10.0
- [ ] (2) Restore dependencies and build project
- [ ] (3) Run benchmarks and record baseline results
- [ ] (4) Project builds and benchmarks run successfully (**Verify**)
- [ ] (5) Commit changes with message: "chore(net10): Tier 5 benchmarks upgrade"

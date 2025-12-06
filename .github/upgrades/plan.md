# .NET Upgrade Plan for Azure Functions Host (WebJobs.Script solution)

## 1. Executive Summary

- Scenario: Upgrade the solution to .NET 10 RTM, focusing on `WebJobs.Script.WebHost` and its dependency chain.
- Scope: 6 primary projects plus 1 benchmark project and 3 test projects. Current frameworks largely `net8.0`; one shared abstractions package targets `netstandard2.0` (external package, not part of solution). Assessment proposes `net10.0` for all solution projects.
- Target State: All projects upgraded to their proposed target frameworks; packages updated per assessment; solution builds cleanly; tests pass.
- Selected Strategy: Bottom-Up Strategy. Rationale: Multi-project solution with clear dependency tiers and test projects on top. Reduces risk by upgrading foundation libraries first, then services, then applications, then tests.
- Complexity Assessment: Medium-High. Justification: Large codebase sizes (e.g., `WebHost` ~29k LOC; `Script` ~33k LOC), ASP.NET Core app changes, several deprecated and incompatible packages requiring replacements, and integration test breadth.
- Critical Issues:
  - Deprecated packages: `Microsoft.Azure.Storage.File`, `Microsoft.Security.Utilities`, `Microsoft.ApplicationInsights.AspNetCore`, `Microsoft.AspNetCore.Mvc.WebApiCompatShim`, `Microsoft.Azure.DocumentDB.Core`.
  - Incompatible packages: `Mono.Posix.NETStandard` (no supported version for net10), `Microsoft.AspNet.WebApi.Core` in integration tests.
  - Package functionality included in framework: `System.Net.NameResolution`.
- Recommended Approach: Incremental migration in phases (Bottom-Up), strictly respecting dependency order, batching operations per tier. Test projects last.

## 2. Migration Strategy

### 2.1 Approach Selection

- Chosen Strategy: Bottom-Up Strategy.
- Strategy Rationale: Clear dependency hierarchy; multiple projects; need to minimize cross-tier breakages; allow tier validation checkpoints.
- Strategy-Specific Considerations:
  - Upgrade tiers in strict order; batch operations per tier.
  - Validate each tier with builds and available tests before proceeding.
  - Document lessons learned per tier for later tiers.

- Determination:
  - Number of projects and dependencies warrant incremental bottom-up.
  - Codebase size and ASP.NET Core app complexity favor staged upgrades.

### 2.2 Dependency-Based Ordering

Critical paths:
- `WebJobs.Script` (core library) is a foundational dependency for `Grpc` and `WebHost`; many tests depend on both.
- `WebJobs.Script.Grpc` depends on `WebJobs.Script` and is consumed by `WebHost` and tests.
- `WebJobs.Script.WebHost` depends on `Script` and `Grpc`, and is consumed by benchmarks and tests.
- Test projects depend on application and libraries. Benchmarks depend on `Script` and `WebHost`.

No circular dependencies were identified in assessment.

### 2.3 Parallel vs Sequential Execution

- Parallel within tiers is acceptable (e.g., Tier 1 may include only `WebJobs.Script` as a leaf from project references perspective; operations are still batched for that tier).
- Sequential across tiers is mandatory: do not start Tier N+1 before Tier N validation.

## 3. Detailed Dependency Analysis

### 3.1 Dependency Graph Summary

```
Tier 4: [WebJobs.Script.Tests] [WebJobs.Script.Tests.Integration] [WebJobs.Script.Tests.Shared] [Microsoft.Azure.WebJobs.Script.Benchmarks]
          ↓              ↓                ↓                              ↓
Tier 3:                        [WebJobs.Script.WebHost]
                                  ↓        ↓
Tier 2:                    [WebJobs.Script.Grpc]
                                  ↓
Tier 1:                    [WebJobs.Script]
```

Placement validation:
- Tier 1: `WebJobs.Script` has zero project dependencies and is consumed by `Grpc`, `WebHost`, tests, and benchmarks.
- Tier 2: `WebJobs.Script.Grpc` depends only on Tier 1; is consumed by `WebHost` and tests.
- Tier 3: `WebJobs.Script.WebHost` depends on Tier 1 and Tier 2; is consumed by tests and benchmarks.
- Tier 4: Test projects and benchmarks depend on Tier 3 and lower.

### 3.2 Project Groupings (Phases)

- Phase 0: Preparation and SDK validation.
- Phase 1 (Tier 1): `src/WebJobs.Script/WebJobs.Script.csproj`
- Phase 2 (Tier 2): `src/WebJobs.Script.Grpc/WebJobs.Script.Grpc.csproj`
- Phase 3 (Tier 3): `src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj`
- Phase 4 (Tier 4): `test/WebJobs.Script.Tests.Shared/WebJobs.Script.Tests.Shared.csproj`, `test/WebJobs.Script.Tests/WebJobs.Script.Tests.csproj`, `test/WebJobs.Script.Tests.Integration/WebJobs.Script.Tests.Integration.csproj`
- Phase 5: `perf/WebJobs.Script.Benchmarks/Microsoft.Azure.WebJobs.Script.Benchmarks.csproj`

## 4. Project-by-Project Migration Plans

### Project: src/WebJobs.Script/WebJobs.Script.csproj

Current State
- Dependencies: None (project references); many NuGet dependencies.
- Dependants: `WebHost`, `Grpc`, `Tests`, `Benchmarks`.
- Package Count: Numerous; see assessment table.
- LOC: ~33,398; Files: 398.

Target State
- Target Framework: net10.0
- Updated Packages: `Microsoft.Extensions.Http.Polly` (8.0.7 → 10.0.0); address deprecated/incompatible packages.

Migration Steps
1. Prerequisites
   - Ensure .NET 10 SDK available; global.json compatibility validated.
2. Framework Update
   - Update `TargetFramework` in `WebJobs.Script.csproj` to `net10.0`.
3. Package Updates
   - Update `Microsoft.Extensions.Http.Polly`: 8.0.7 → 10.0.0 (reason: aligns with .NET 10, assessment recommendation).
   - Replace deprecated `Microsoft.ApplicationInsights.AspNetCore` (2.22.0 → 2.23.0 or the latest compatible per assessment deprecation note). Note: Application Insights SDK versions generally compatible; verify binding redirects not needed in .NET.
   - Remove `Microsoft.AspNetCore.Mvc.WebApiCompatShim` (deprecated). If functionality required, refactor APIs to pure ASP.NET Core MVC equivalents.
   - Remove `Mono.Posix.NETStandard` (incompatible). Investigate usages; replace with supported APIs (e.g., `System.Runtime.InteropServices`, `System.IO`, `System.Diagnostics.Process`), or conditionalize platform-specific code.
4. Expected Breaking Changes
   - API differences from removal of WebApiCompatShim; controller/model binding adjustments.
   - Removal of Mono.Posix APIs; path/permissions/process APIs may need replacements.
5. Code Modifications
   - Migrate any uses of `Mono.Posix` types (e.g., `Mono.Unix` classes) to .NET APIs.
   - Remove Web API compat shim usages and update MVC configuration.
6. Testing Strategy
   - Build and run unit tests (in higher tiers after they migrate). For now, compile clean.
7. Validation Checklist
   - [ ] Dependencies resolve
   - [ ] Builds without errors/warnings

---

### Project: src/WebJobs.Script.Grpc/WebJobs.Script.Grpc.csproj

Current State
- Dependencies: `WebJobs.Script`
- Dependants: `WebHost`, `Tests`
- Package Count: Small; `Grpc.AspNetCore`, `Microsoft.Azure.WebJobs.Rpc.Core`, analyzers.
- LOC: ~10,610; Files: 102.

Target State
- Target Framework: net10.0
- Updated Packages: None flagged (compatible).

Migration Steps
1. Prerequisites
   - Phase 1 complete; `WebJobs.Script` on net10.0.
2. Framework Update
   - Update `TargetFramework` to `net10.0`.
3. Package Updates
   - None required per assessment.
4. Expected Breaking Changes
   - gRPC server/client APIs mostly stable; check ASP.NET Core hosting changes for net10.
5. Code Modifications
   - Validate startup and DI patterns.
6. Testing Strategy
   - Ensure builds; run any unit tests if present.
7. Validation Checklist
   - [ ] Dependencies resolve
   - [ ] Build clean

---

### Project: src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj

Current State
- Dependencies: `WebJobs.Script`, `WebJobs.Script.Grpc`
- Dependants: Tests and benchmarks
- Package Count: Several ASP.NET Core and Azure worker packages
- LOC: ~29,221; Files: 329.

Target State
- Target Framework: net10.0
- Updated Packages: `Microsoft.AspNetCore.Authentication.JwtBearer` (6.0.0 → 10.0.0), `Microsoft.AspNetCore.Mvc.NewtonsoftJson` (8.0.1 → 10.0.0), remove/replace deprecated packages.

Migration Steps
1. Prerequisites
   - Phase 1 and 2 complete.
2. Framework Update
   - Update `TargetFramework` to `net10.0`.
3. Package Updates (tier-scoped)
   - Microsoft.AspNetCore.Authentication.JwtBearer: 6.0.0 → 10.0.0 (reason: aligns with ASP.NET Core for net10).
   - Microsoft.AspNetCore.Mvc.NewtonsoftJson: 8.0.1 → 10.0.0.
   - System.Net.NameResolution: remove (functionality in framework).
   - Microsoft.Azure.Storage.File: deprecated; plan migration toward `Azure.Storage.Files.Shares`. If temporary retention needed, note risk; otherwise replace APIs.
   - Microsoft.Security.Utilities: deprecated; migrate to `Microsoft.Security.Utilities.Core` and update usages.
4. Expected Breaking Changes
   - ASP.NET Core 10 program/hosting and auth stack changes; JWT handler defaults; configuration binding.
   - Newtonsoft.Json integration in MVC may have option changes.
   - Storage File v11 → Azure.Storage.Files.Shares v12 API differences.
5. Code Modifications
   - Update `Program.cs`/startup configuration to current minimal hosting model for net10 if applicable.
   - Adjust JWT authentication configuration per `JwtBearerOptions` changes.
   - Replace any direct `System.Net.NameResolution` usages.
   - Refactor file share storage logic to `Azure.Storage.Files.Shares` SDK.
   - Replace `Microsoft.Security.Utilities` types with `Microsoft.Security.Utilities.Core` equivalents.
6. Testing Strategy
   - Smoke test app starts; validate endpoints.
   - Integration tests will run in Phase 4 after their upgrade.
7. Validation Checklist
   - [ ] Build clean
   - [ ] App starts locally

---

### Project: test/WebJobs.Script.Tests.Shared/WebJobs.Script.Tests.Shared.csproj

Target State
- Target Framework: net10.0
- Packages: `Microsoft.Azure.Storage.Blob` deprecated; consider migration to `Azure.Storage.Blobs` for any runtime usage (though test-only).

Steps
1. Update `TargetFramework` to `net10.0`.
2. Replace deprecated storage blob package with `Azure.Storage.Blobs` if used in test helpers; adapt APIs.
3. Validate build for consumers.

---

### Project: test/WebJobs.Script.Tests/WebJobs.Script.Tests.csproj

Target State
- Target Framework: net10.0
- Packages: Update `Microsoft.AspNetCore.TestHost` 8.0.1 → 10.0.0; replace deprecated `Microsoft.Azure.Storage.Blob` if used.

Steps
1. Update `TargetFramework` to `net10.0`.
2. Update packages per above.
3. Run unit tests.

---

### Project: test/WebJobs.Script.Tests.Integration/WebJobs.Script.Tests.Integration.csproj

Target State
- Target Framework: net10.0
- Packages: Update `Microsoft.AspNetCore.TestHost` 8.0.1 → 10.0.0; address incompatible `Microsoft.AspNet.WebApi.Core` (no supported version) by removing dependency and migrating any legacy OWIN/WebAPI-based tests to ASP.NET Core test infrastructure or HTTP client directly; `Microsoft.Azure.DocumentDB.Core` is deprecated — prefer `Microsoft.Azure.Cosmos`.

Steps
1. Update `TargetFramework` to `net10.0`.
2. Update `Microsoft.AspNetCore.TestHost` to 10.0.0.
3. Remove `Microsoft.AspNet.WebApi.Core`; refactor tests to ASP.NET Core concepts.
4. Plan migration path for DocumentDB dependencies to Cosmos SDK (if tests require it).
5. Run integration tests.

---

### Project: perf/Microsoft.Azure.WebJobs.Script.Benchmarks.csproj

Target State
- Target Framework: net10.0
- Packages: None flagged for update.

Steps
1. Update `TargetFramework` to `net10.0`.
2. Validate benchmarks build and run.

## 5. Risk Management

### 5.1 High-Risk Changes

| Project | Risk | Mitigation |
|---------|------|------------|
| WebJobs.Script | Removal of `Mono.Posix.NETStandard` and `WebApiCompatShim` causing API changes | Identify usages early; replace with .NET APIs; add unit tests around affected code pathways |
| WebJobs.Script.WebHost | ASP.NET Core 10 changes; storage file SDK migration | Incremental changes; verify startup; add integration smoke tests; feature toggles if needed |
| Tests.Integration | Removal of legacy WebApi Core; Cosmos migration | Refactor tests to ASP.NET Core; use `Microsoft.AspNetCore.TestHost`; minimize external dependencies |

### 5.3 Contingency Plans
- If `Mono.Posix` removal blocks functionality, consider conditional compilation with `OperatingSystem.IsWindows/Linux` checks and platform-specific implementations.
- If `Microsoft.Azure.Storage.File` migration is large, temporarily isolate usage behind an adapter; swap implementation to `Azure.Storage.Files.Shares` progressively.
- If integration test migrations are extensive, split into sub-phases and prioritize critical scenarios.

## 6. Testing and Validation Strategy

### 6.1 Phase-by-Phase Testing
- Phase 1: Build `WebJobs.Script`; run any library unit tests if present.
- Phase 2: Build `Grpc` with updated references.
- Phase 3: Build and smoke-test `WebHost` (start app, basic endpoints).
- Phase 4: Run unit and integration tests; fix failures.
- Phase 5: Run benchmarks for regression checks.

### 6.2 Smoke Tests
- Build succeeds.
- WebHost starts and responds on health endpoints.

### 6.3 Comprehensive Validation
- All tests pass; performance acceptable; security scans clean; no warnings.

## 7. Timeline and Effort Estimates

### 7.1 Per-Project Estimates

| Project | Complexity | Estimated Time | Dependencies | Risk Level |
|---------|------------|---------------|--------------|------------|
| WebJobs.Script | High | 2-4 days | None | High |
| WebJobs.Script.Grpc | Medium | 1 day | Script | Medium |
| WebJobs.Script.WebHost | High | 3-5 days | Script, Grpc | High |
| Tests.Shared | Low | 0.5 day | WebHost | Low |
| Tests | Medium | 2-3 days | Script, Grpc, WebHost | Medium |
| Tests.Integration | High | 3-4 days | Script, Grpc, WebHost | High |
| Benchmarks | Low | 0.5 day | Script, WebHost | Low |

### 7.2 Phase Durations
- Phase 1: 2-4 days
- Phase 2: 1 day
- Phase 3: 3-5 days
- Phase 4: 5-7 days
- Phase 5: 0.5 day
- Total: ~12-17 days (+ buffer 20%).

### 7.3 Resource Requirements
- Developers familiar with ASP.NET Core 10, Azure Storage SDKs, Cosmos SDK, and gRPC.
- Test engineers to refactor and validate integration tests.

## 8. Source Control Strategy

### 8.1 Guidance
- Use a dedicated upgrade branch for all changes; create tier-based PRs.

### 8.2 Branching Strategy
- Main upgrade branch: `upgrade-to-NET10`.
- Create one PR per phase (Tier).

### 8.3 Commit Strategy
- Default: Commit after each tier task.
- Commit message format: `chore(net10): [tier] [project] short description`.

### 8.4 Review and Merge Process
- Require reviews; run CI for each PR; ensure green builds and tests.

## 9. Success Criteria

### 9.1 Strategy-Specific Success Criteria
- Tiers upgraded in order; each tier validated before proceeding.

### 10.2 Technical Success Criteria
- [ ] All projects target net10.0
- [ ] All packages updated per assessment
- [ ] Zero security vulnerabilities
- [ ] Builds succeed without errors/warnings
- [ ] All automated tests pass
- [ ] Performance within acceptable thresholds

### 10.3 Quality Criteria
- [ ] Code quality maintained or improved
- [ ] Test coverage maintained or improved
- [ ] Documentation updated

### 10.4 Process Criteria
- [ ] Bottom-Up Strategy followed
- [ ] Source control strategy followed

## 10. Per-Tier Specifications (Bottom-Up)

### Tier 1 (Leaf nodes - no internal project references)
- Projects: `WebJobs.Script`
- Depends on: External packages only (Tier 0)
- Placement: No project references; consumed by many.
- Estimated complexity: High

Upgrade Details
- Framework: net8.0 → net10.0
- Package updates:
  - Microsoft.Extensions.Http.Polly: 8.0.7 → 10.0.0 (tier-wide: Script)
  - Replace deprecated: ApplicationInsights AspNetCore; remove WebApiCompatShim; remove Mono.Posix.NETStandard
- Breaking changes:
  - Removal of deprecated/incompatible packages.
- Code modifications: API replacements; MVC compat removal.

Validation Requirements
- Build success; no warnings; downstream consumers compile with old TFMs until upgraded.

### Tier 2 (Depends only on Tier 1)
- Projects: `WebJobs.Script.Grpc`
- Depends on: Tier 1 (`Script`)
- Placement: References Script only.
- Estimated complexity: Medium

Upgrade Details
- Framework: net8.0 → net10.0
- Package updates: None flagged
- Breaking changes: Minimal

Validation Requirements
- Build success; integration with Tier 1 verified.

### Tier 3 (Applications - depends on Tiers 1 & 2)
- Projects: `WebJobs.Script.WebHost`
- Depends on: Tier 1 & 2
- Estimated complexity: High

Upgrade Details
- Framework: net8.0 → net10.0
- Package updates:
  - JwtBearer: 6.0.0 → 10.0.0
  - Mvc.NewtonsoftJson: 8.0.1 → 10.0.0
  - Remove System.Net.NameResolution
  - Replace deprecated: Microsoft.Azure.Storage.File → Azure.Storage.Files.Shares; Microsoft.Security.Utilities → Microsoft.Security.Utilities.Core
- Breaking changes: ASP.NET Core hosting/auth; storage SDK migration.

Validation Requirements
- App start; basic endpoints; authentication flows.

### Tier 4 (Tests and supporting tools)
- Projects: `WebJobs.Script.Tests.Shared`, `WebJobs.Script.Tests`, `WebJobs.Script.Tests.Integration`
- Depends on: Tiers 1–3
- Estimated complexity: Medium-High

Upgrade Details
- Framework: net8.0 → net10.0
- Package updates:
  - TestHost: 8.0.1 → 10.0.0 (Tests + Integration)
  - Replace deprecated: Storage.Blob → Azure.Storage.Blobs
  - Remove incompatible: Microsoft.AspNet.WebApi.Core (Integration)
  - Deprecated: DocumentDB.Core → plan migration to Cosmos SDK if needed

Validation Requirements
- Unit/integration tests green; no flakiness; security scan clean.

### Tier 5 (Benchmarks)
- Projects: `Microsoft.Azure.WebJobs.Script.Benchmarks`
- Depends on: Tiers 1–3
- Estimated complexity: Low

Upgrade Details
- Framework: net8.0 → net10.0
- Package updates: None flagged

Validation Requirements
- Benchmarks run; record baseline.

## 11. Execution Sequence and Completion Criteria

Ordering rules
1. Upgrade tiers in strict order (Tier 1 → 5).
2. Tier completion criteria:
   - Build success; warnings addressed
   - Minimal smoke tests pass
   - Document lessons learned
3. Between-tier validation:
   - Verify lower tiers remain stable
   - Ensure higher tiers (still on old framework) can consume upgraded lower tiers without breaking public API expectations
4. Deployment cadence:
   - Create PR per tier; merge sequentially after validation.

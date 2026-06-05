## General

* Make only high-confidence suggestions when reviewing code changes.
* Always use the latest version of C#, currently C# 13 features.
* Never change global.json unless explicitly asked to.
* Never change package.json or package-lock.json files unless explicitly asked to.
* Never change NuGet.config files unless explicitly asked to.
* For C# string comparisons, always use string.Equals with an appropriate, explicit, `StringComparison`.

## Formatting

* Apply code-formatting style defined in `.editorconfig`.
* Prefer file-scoped namespace declarations and single-line using directives.
* Insert a newline before the opening curly brace of any code block (e.g., after `if`, `for`, `while`, `foreach`, `using`, `try`, etc.).
* Ensure that the final return statement of a method is on its own line.
* Use pattern matching and switch expressions wherever possible.
* Use `nameof` instead of string literals when referring to member names.
* Ensure that XML doc comments are created for any public APIs. When applicable, include `<example>` and `<code>` documentation in the comments.

### Nullable Reference Types

* Declare variables non-nullable, and check for `null` at entry points.
* Always use `is null` or `is not null` instead of `== null` or `!= null`.
* Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

### Testing

* Do not emit "Act", "Arrange" or "Assert" comments.
* Use Moq for mocking in tests.
* Copy existing style in nearby files for test method names and capitalization.
* Do not use private reflection (e.g., `BindingFlags.NonPublic`, `GetField`, `GetProperty` with non-public flags) to access internal state in tests. If something needs to be tested, make it accessible through public or internal APIs, test-specific seams, or refactor the design to be testable without reflection.

## Dependencies & Patterns

* Prefer `System.Text.Json` over `Newtonsoft.Json` for new code. Only use Newtonsoft when interfacing with existing APIs that require it (e.g., `JObject`-based shared helpers).
* In this repository, `Microsoft.Azure.WebJobs.Script.IEnvironment` is our internal interface for accessing process environment data (for example, environment variables and related flags). Do not introduce new usage of `IEnvironment` for reading configuration; prefer the standard `Microsoft.Extensions.Configuration.IConfiguration` abstraction instead. Existing `IEnvironment` usage in legacy code is acceptable but should not be extended beyond its current scope.
* Do not use event-based communication (`IScriptEventManager` pub/sub) for new component coordination. Prefer direct method calls or `await`-based flows. The event manager's keyed state store (`TryAddWorkerState`/`TryGetWorkerState`) is acceptable when needed by existing infrastructure.

## Prompts & Workflows

* When the user reports that `Verify_DepsJsonChanges` failed, or asks to update / refresh the WebHost `deps.json` baseline, follow the workflow in `.github/prompts/update-deps-json.prompt.md`.

## JIT Trace Generation for Cold Start Improvement

### Goal

Improve **cold start latency** of Azure Function Apps by reducing **JIT compilation work** that occurs when the Functions Host processes the first (cold) HTTP request.

This work primarily focuses on the **CI pipeline** that:
1) collects a PerfView trace in perf lab, and  
2) converts that trace into a `.jittrace` file using `dotnet-pgo`.

### Background: Placeholder Mode + PreJIT

In production, the Functions Host can run in **placeholder mode**:
- Host process is started and waiting for a customer app deployment — no customer payload/code is loaded yet.
- While in placeholder mode, the Azure platform sends a **warmup call** to the host.
- `HostWarmupMiddleware.WarmupInvoke()` (in `src/WebJobs.Script.WebHost/Middleware/HostWarmupMiddleware.cs`) handles the warmup call and triggers `PreJitPrepare()`, which loads `.jittrace` files and calls `JitTraceRuntime.Prepare()` (in `src/WebJobs.Script.WebHost/PreJIT/JitTraceRuntime.cs`). This method reads the `.jittrace` file (a list of method signatures) and calls `RuntimeHelpers.PrepareMethod()` for each entry, forcing Tier-1 (optimized) JIT compilation of those methods. Note: `TieredCompilation` is intentionally set to `false` in the WebHost project to ensure `PrepareMethod` produces optimized code.
- When the **specialization event** arrives (the first customer request, i.e., the cold start), the number of methods that still need JIT compilation is significantly reduced because they were already pre-JITted during warmup.

The checked-in `.jittrace` files live in `src/WebJobs.Script.WebHost/PreJIT/` (`coldstart.jittrace` for Windows, `linux.coldstart.jittrace` for Linux).

### Existing Production Workflow (Legacy Approach)

The production `.jittrace` file has historically been generated using a **private stamp** workflow:
1. Provision a private stamp environment (Azure infrastructure with IIS hosting on Windows)
2. Start a Function App in placeholder mode
3. Collect a **PerfView** trace (`.etl` file) while triggering a cold start request — this captures which methods are JIT-compiled during the cold start code path
4. Convert the PerfView trace into a `.jittrace` file using a debug build of **`dotnet-pgo`** — this tool reads the JIT compilation events from the trace and emits the list of method signatures that were compiled

This workflow is slow due to private stamp provisioning and Azure infrastructure dependencies (e.g., IIS hosting on Windows). The goal is to replace it with the perf lab approach below.

### Perf Lab Approach (New — ADO Pipeline)

The perf lab replaces private stamps with an **Azure DevOps pipeline** (`eng/ci/templates/official/jobs/run-coldstart.yml`):

1. **Build & publish the host** with `-p:PlaceholderSimulation=true` — this enables `Utility.IsInPlaceholderSimulationMode`, allowing the host to behave as if it were in placeholder mode without real Azure infrastructure.
2. **Build & publish a benchmark Function App** (e.g., `HelloHttpNet9` with auth enabled for jittrace generation).
3. **Start the host on IIS** (Windows) or directly (Linux) on the ADO agent machine. On Windows, the host runs InProcess behind IIS/ANCM (matching production). Environment variables are injected via ANCM site-level `environmentVariables` configuration. For jittrace generation runs, `AzureWebJobsStorage` is configured from Key Vault so the authentication code path executes end-to-end.
4. **Send a warmup call** to the host (polling until ready) — this triggers the same `HostWarmupMiddleware` warmup path that runs in production. On Windows, the `w3wp` PID is captured after warmup for trace collection.
5. **Collect a perf trace** around the cold start trigger:
   - Windows: PerfView (`.etl`) and/or `dotnet-trace` (`.nettrace`) — `dotnet-trace` attaches by `w3wp` PID
   - Linux: `dotnet-trace` (`.nettrace`)
6. **Trigger the cold start**:
   - Windows: send one POST to the function endpoint with `?forcespecialization` and `AzureWebJobsScriptRoot` in the JSON body (handled by `SpecializationSimulatorMiddleware`), so specialization and function invocation happen in the same request.
   - Linux: send a POST to `/admin/instance/assign?code=test` with an encrypted `HostAssignmentContext`, then send a GET to the function endpoint without `?forcespecialization`.
7. **Generate the `.jittrace`** using `func-cold-start-analyzer --generate-jittrace` — the analyzer tool reads the `.nettrace` file, extracts JIT compilation events, and produces the `.jittrace` file.
8. **Merge jittrace files** from multiple function app scenarios via `eng/ci/templates/official/jobs/merge-jittrace.yml` — the merged file is deduplicated and published as a pipeline artifact.

Perf lab is not identical to production but aims for **parity in outcomes** (comparable JIT count and cold start latency). On Windows, running behind IIS/ANCM provides closer parity with the production hosting model.

### Success Criteria

Evaluate changes using:
- **JIT count for the Functions WebHost (host/runtime) process**
  - Production baseline: ~150 JITted methods
- **Cold start latency**
  - Production baseline: ~500 ms

Aim for **parity or near-parity** using perf lab–generated `.jittrace`.

### Validation & Trace Analysis

A trace analyzer tool is used to validate:
- which methods were JITted in the WebHost process
- aggregated JIT counts per method

#### Exclusions / Constraints

Do not attempt to pre-JIT methods that require customer payloads or are loaded from customer code paths.
Examples that may appear in traces and should be excluded from "pre-JIT success" accounting:
- `Microsoft.Azure.WebJobs.Extensions.FunctionMetadataLoader`

When comparing JIT counts, focus on **host-owned methods** that can realistically be pre-JITted in placeholder mode.

### Agent Guidance for Editing `run-coldstart.yml`

When modifying the pipeline YAML:

- Prefer **small, incremental** edits that can be validated quickly.
- Preserve existing **step ordering** unless changing it is required and justified.
- Avoid **timing-dependent** behavior where possible:
  - minimize arbitrary sleeps
  - if a wait is required, prefer bounded retries with clear readiness checks
- Keep the pipeline **deterministic and repeatable**:
  - pin tool versions when feasible
  - make paths explicit
  - fail fast with clear logs when prerequisites are missing

### What Not To Do

- Do not propose solutions that require private stamp provisioning as part of the perf lab pipeline.
- Do not "fix" parity by masking measurement (e.g., excluding large categories without justification).
- Do not broaden scope into unrelated CI templates unless the change is necessary for `run-coldstart.yml`.


# Compiled public API baselines

This project is the package-complete compiled public API gate for the host. It inventories the
complete public and protected API of every first-party assembly that the package pipeline actually
ships, and it enforces the narrow set of records that Azure Functions Core Tools compiles against.

It is intentionally different from the Phase 0 migration inventory under
`test/WebJobs.Script.Tests/StaticAnalysis/EnvironmentMigration`. That inventory is a focused,
source-driven view of the `IEnvironment` migration surface. This project is a package-complete
compiled compatibility gate.

## What is inventoried

`ShippedAssemblyManifest.json` is the authoritative package-to-assembly map. It is derived from the
explicit `dotnet pack` project list in `eng/ci/templates/official/jobs/build-artifacts-windows.yml`
and cross-checked against each project file.

| Package | Package project | Baselined assembly | Package asset |
|---|---|---|---|
| `Microsoft.Azure.WebJobs.Script` | `src/WebJobs.Script` | `Microsoft.Azure.WebJobs.Script.dll` | `lib/net10.0/Microsoft.Azure.WebJobs.Script.dll` |
| `Microsoft.Azure.WebJobs.Script.WebHost` | `src/WebJobs.Script.WebHost` | `Microsoft.Azure.WebJobs.Script.WebHost.dll` | `lib/net10.0/Microsoft.Azure.WebJobs.Script.WebHost.dll` |
| `Microsoft.Azure.WebJobs.Script.Grpc` | `src/WebJobs.Script.Grpc` | `Microsoft.Azure.WebJobs.Script.Grpc.dll` | `lib/net10.0/Microsoft.Azure.WebJobs.Script.Grpc.dll` |
| `Microsoft.Azure.WebJobs.Script.Abstractions` | `src/WebJobs.Script.Abstractions` | `Microsoft.Azure.WebJobs.Script.Abstractions.dll` | `lib/netstandard2.0/Microsoft.Azure.WebJobs.Script.Abstractions.dll` |
| `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator` | `tools/ExtensionsMetadataGenerator/src/ExtensionsMetadataGenerator` | `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.dll` (`net46`) | `tools/net46/Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.dll` |
| `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator` | `tools/ExtensionsMetadataGenerator/src/ExtensionsMetadataGenerator` | `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.dll` (`netstandard2.0`) | `tools/netstandard2.0/Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.dll` |
| `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator` | `tools/ExtensionsMetadataGenerator/src/ExtensionsMetadataGenerator` | `Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.Console.dll` | `tools/netstandard2.0/generator/Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator.Console.dll` |

The ExtensionsMetadataGenerator package project sets `IncludeBuildOutput=false`, which suppresses
only the default `lib` asset. Its `_CollectRuntimeDependencies` target explicitly packs every DLL
from both package-project target frameworks, including the package project's own MSBuild task
assembly and the console tool copied under the `netstandard2.0/generator` directory. All three
first-party assets are baselined independently.

Third-party dependency assemblies copied into a package are governed by package dependency and
version review. They are not re-baselined as this repository's public API.

`ShippedAssemblyManifestTests` mechanically fails when a newly packed project, a newly shipped
first-party assembly, a new project under a shipped source root, or a stale baseline appears.

## Baseline format

Each assembly has one line-oriented baseline under `Baselines/`, written as UTF-8 without a byte
order mark and with LF line endings. Every record is one line:

```text
<kind> | <identity> | <signature>
```

`kind` is `assembly`, `forwarder`, `type`, `constructor`, `field`, `property`, `event`, or `method`.
`identity` is unique within the assembly and starts with the declaring type, so records are grouped
per type and a normal change produces a small per-type diff. Records are sorted with ordinal
comparison, so reflection or source ordering never changes the output.

Assemblies are loaded from the exact Release `TargetPath` recorded in the manifest, using an
isolated `AssemblyLoadContext` with an `AssemblyDependencyResolver`.

Recorded:

- assembly simple name, `AssemblyVersion`, culture, public key token, target framework, and type
  forwarder target edges;
- effective type accessibility through every containing type, nesting, kind
  (class, record class, struct, record struct, ref struct, interface, enum, delegate),
  static/abstract/sealed/readonly/byref-like state, generic arity, variance and constraints, enum
  underlying type, direct base type, and direct interfaces;
- public, protected, and protected-internal constructors;
- methods including operators and conversions, virtual/abstract/override/sealed state, generic
  arity and constraints, return type, nullability, `ref` and `ref readonly` returns, and parameter
  names, types, nullability, `ref`/`out`/`in`/`params`/optional state and default values;
- properties and indexers with accessor-specific visibility, init-only state, index parameters, and
  ref returns;
- events with accessor visibility;
- fields including const values (including attribute-encoded decimal and date-time constants), enum
  values, and readonly state;
- compatibility-significant attributes: `Obsolete`, `EditorBrowsable`, `Extension`, `DefaultMember`,
  `Flags`, `StructLayout` when it is not the default for the type kind, `ComVisible`, `Guid`,
  `InterfaceType`, `CLSCompliant`, `RequiredMember`, `SetsRequiredMembers`, and P/Invoke calling
  metadata. `params`, `in`, `readonly`, `ref struct`, and `init` are rendered as modifiers rather
  than as raw attributes.

Nullability is recorded separately from CLR type identity: `!` marks a non-nullable and `?` a
nullable reference type. An unannotated reference type is oblivious.

Deliberately excluded:

- file and informational versions, source paths, timestamps, module version ids, and other
  per-build identity;
- private, internal, and private-protected members;
- property and event accessor methods, which are represented by their owning member;
- private backing fields, state machines, closures, and non-visible compiler implementation types;
- the `value__` field of an enum, whose type is already recorded by the enum declaration.

Public and protected generated members, including record and protobuf members, remain recorded
because they are externally callable. `sealed` on a member means the compiled method is
`virtual final`, which is how the compiler emits a non-virtual interface implementation.

## Core Tools compatibility contract

`CoreToolsCompatibilityContract.json` records the independent audit of current Azure Functions Core
Tools `main`, separately identifies the older historical `vnext` evidence, and records the pinned host
package versions, three `main` call sites, and six compiled records those call sites require. They are
the only Core Tools-required records in the current environment-migration surface, not proof that no
other external consumers exist and not permanent public contracts.

`CoreToolsCompatibilityTests` requires those records to remain in the compiled snapshot and checked-in
baselines with public accessibility. It also hard-codes the feature-owned removal requirements:

- move Core Tools feature-flag usage to the existing one-argument configuration path;
- ship a public configuration-only bundle options parser before migrating the helper call;
- ship a public bundle resolver/factory with narrow local/Core Tools runtime options before migrating
  the manager call and its bundle tests;
- remove `IEnvironment`, `SystemEnvironment`, and `SystemEnvironment.Instance` only after all three
  call sites migrate, a Core Tools release contains those migrations, host-internal use reaches zero,
  and the compiled-baseline change is reviewed.

The contract also records human review policy for existing feature-owned integration paths
(`ScriptApplicationHostOptions`, the supported Add/UseWebJobsScriptHost builder path, and worker
Options/configuration hooks) or additive successors; it rejects replacing them with a generic
environment/capability API. Those paths remain visible in the package-complete baseline, but are
intentionally not additional hard-preserve records beyond the audited six. Everything else in the
compiled baseline remains reviewable under the maintainer-approved internal-zero policy. The updater
cannot bless one of the six records merely by changing narrative text because the normal test rerun
enforces exact record-to-requirement mappings and gates.

## Compatibility classification

`IEnvironmentCompatibilityClassification.json` classifies the complete current migration surface
with no wildcards and no default category:

| Category | Entries |
|---|---:|
| `core-tools-required` | 6 |
| `host-internal-exported-surface` | 81 |
| `exported-legacy-static-bridge` | 15 |
| `effectively-internal` | 77 |
| `test-only-legacy-seam` | 12 |
| **Total** | **191** |

`IEnvironmentCompatibilityClassificationTests` fails on new and stale entries in both directions and
also recomputes the compiled migration signatures and `EnvironmentExtensions` helpers directly from
the Release assemblies, so neither the ledger nor the Phase 0 inventory can drift silently.

## Refreshing a baseline

Normal test runs only compare and fail; they never write a checked-in baseline. After reviewing an
intentional API change, run:

```powershell
eng\script\update-public-api-baselines.ps1
```

The script builds in Release, writes candidates to `out/public-api-candidates`, verifies the
candidate set and its format, enforces the Core Tools preserve set, copies the candidates into
`Baselines/`, and reruns the comparison with no update mode. Review the resulting diff before
committing it.

To prove the gate fails without touching tracked files, point it at a mutated copy:

```powershell
$env:PUBLIC_API_BASELINE_DIRECTORY = "$PWD\out\public-api-mutated"
dotnet test test\WebJobs.Script.PublicApi.Tests\WebJobs.Script.PublicApi.Tests.csproj -c release `
  --filter "FullyQualifiedName~PublicApiBaselineTests.BaselinesMatchCompiledPublicApi"
Remove-Item Env:\PUBLIC_API_BASELINE_DIRECTORY
```

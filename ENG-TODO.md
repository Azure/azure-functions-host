# Engineering TODO

## Overview

In preparation for splitting the host between in-proc and out-of-proc, we want to perform some prep engineering work. The end goal is to provide a comprehensive, up to date, and compliant engineering system to improve the day-to-day development experience on this repository. Due to the limited time before host split, we will be performing only engineering items that would cause merge headaches if not done before the split.

See https://github.com/jviau/dotnet-worker-layout/blob/main/Overview.md for long-term goals.

This document will focus only on what we want to accomplish pre-host split, which essentially amounts to 

## Goals

1. Re-structure repo layout. See [here](#layout).
2. Keep all existing CI, only updating script/yml locations and the paths they use internally as necessary.
3. Add global.json
4. Add common build targets -> versioning / release, shared, internals-visible-to
5. **NEW** add Directory.Packages.props

## Non-Goals

All items below are cut from this work as they will be too expensive to do in time. Instead we will need to do them in parallel on both branches post split.

1. Re-write CI.
   1. Will be needed eventually for 1es compliance
2. Remove stylecop / update .editorconfig analyzers
3. Creating common engineering repo and packages
   1. Can do this later and extract what we need from the host repo

## Layout

```
|- eng/
  |- ci/
  |- tools/
  |- targets/
|- doc
  |- schema/
|- sample/
|- out/ (or .artifacts/) -> <ArtifactsPath>\<Type of Output>\<Project Name>\<Pivots>
  |- bin/
  |- obj/
  |- package/
  |- publish/
  |- temp/ (will be used by existing CI scripts)
|- src/
  |- Script/
    |- Script.csproj
  |- Abstractions/
    |- Script.Abstractions.csproj
  |- Analyzers/
    |- Script.Analyzers.csproj
  |- ExtensionsMetadataGenerator
    |- Script.ExtensionsMetadataGenerator.csproj
  |- Grpc/
    |- Script.Grpc.csproj
  |- WebHost/
    |- Script.WebHost.csproj
|- test/
  |- Abstractions.Tests/
  |- Analyzers.Tests/
  |- Benchmarks/
  |- Common/
  |- Functional.Tests/
  |- Grpc.Tests/
  |- Integration.Tests/
  |- Script.Tests/
  |- WebHost.Tests/
  |- Resources/
    |- Projects/
      |- EmptyScriptRoot/
      |- TestsFunctions/
      |- DotNetIsolated.UnsupportedWorker/
      |- DotNetIsolated/
      |- AssemblyLoadContextRace/
      |- Dependency56/
      |- DependencyA/
      |- MultipleDependencyVersions/
      |- NativeDependencyNoRuntimes/
      |- ReferenceOlderRuntimeAssembly/
      |- WebJobsStartupTests/
|- perf (alternative location for benchmarks)
```

## Plan

1. Add `global.json`, pin to net8.0 SDK
2. 

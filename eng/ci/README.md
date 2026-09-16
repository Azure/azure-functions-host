# Linux standalone artifacts

The official Linux build publishes three independent archives in `drop_linux`:

| Archive | Runtime contract |
| --- | --- |
| `host.linux-x64.tar.gz` | Existing standard WebHost and bundled workers; unchanged. |
| `functions-host.linux-x64.tar.gz` | `Azure.Functions.Host`, its full framework-dependent .NET/ASP.NET Core 10 dependency tree, ReadyToRun assemblies, and an empty `workers/` directory. |
| `worker-proxy.linux-x64.tar.gz` | Native AOT `Azure.Functions.WorkerProxy` and any native publish companions; no language workers. |

Archives have no enclosing directory and retain executable permissions. The Host excludes bundled workers and the standard WebHost executable, deps/runtimeconfig files, and IIS configuration. Its shared WebHost **assembly** remains required. Do not copy only the Host executable or strip its publish dependencies.

`templates/official/jobs/build-artifacts-linux.yml` invokes the shared `templates/steps/publish-linux-artifact.yml` for all three products, with distinct publish directories and archive names. The template uses `DotNetCoreCLI@2` and `ArchiveFiles@2`, matching the existing Linux publishing process.

## Publishing process

- WebHost retains its existing restore, build, publish, and archive commands and settings.
- Functions.Host uses the same separate ReadyToRun restore/build/publish sequence, with `--self-contained false` and `PublishWorkers=false`. The template creates the required empty `workers/` directory before archiving.
- WorkerProxy runs a full `dotnet publish --self-contained true`, including restore, build, and Native AOT compilation; it does not use `--no-build`. The official Linux job installs `clang` and `zlib1g-dev` once before this publish.

All products target release/linux-x64 on the existing Ubuntu 24.04 pool and use the repository SDK installed by `templates/install-dotnet.yml`. The existing `drop_linux` output and 1ES analysis/signed SBOM processing are unchanged, with manifest metadata beside the archives. Image creation and publication are separate concerns.

The Host requires an ASP.NET Core 10 Linux runtime, such as `aspnet:10.0-noble`. WorkerProxy uses `runtime-deps:10.0-noble`. Neither standalone archive includes a language worker, user application, or extension bundle; those are supplied separately.

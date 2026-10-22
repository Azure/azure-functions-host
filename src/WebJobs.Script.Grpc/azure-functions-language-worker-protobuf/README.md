# Azure Functions Language Worker Protobuf

This repository contains the protobuf definition file which defines the gRPC service which is used between the [Azure Functions Host](https://github.com/Azure/azure-functions-host) and the Azure Functions language workers. This repo is shared across many repos in many languages (for each worker) by using git commands.

To use this repo in Azure Functions language workers, follow steps below to add this repo as a subtree (*Adding This Repo*). If this repo is already embedded in a language worker repo, follow the steps to update the consumed file (*Pulling Updates*).

Learn more about Azure Function's projects on the [meta](https://github.com/azure/azure-functions) repo.

## Adding This Repo

From within the Azure Functions language worker repo:
1.	Define remote branch for cleaner git commands
    -	`git remote add proto-file https://github.com/azure/azure-functions-language-worker-protobuf.git`
    -	`git fetch proto-file`
2.	Index contents of azure-functions-worker-protobuf to language worker repo
    -	`git read-tree  --prefix=<path in language worker repo> -u proto-file/<version branch>`
3.	Add new path in language worker repo to .gitignore file
    -   In .gitignore, add path in language worker repo
4.	Finalize with commit
    -	`git commit -m "Added subtree from https://github.com/azure/azure-functions-language-worker-protobuf. Branch: <version branch>. Commit: <latest protobuf commit hash>"`
    -	`git push`

## Pulling Updates

From within the Azure Functions language worker repo:
1.	Define remote branch for cleaner git commands
    -	`git remote add proto-file https://github.com/azure/azure-functions-language-worker-protobuf.git`
    -	`git fetch proto-file`
2.	Pull a specific release tag
    -   `git fetch proto-file refs/tags/<tag-name>`
        -   Example: `git fetch proto-file refs/tags/v1.1.0-protofile`
3.	Merge updates
    -   Merge with an explicit path to subtree: `git merge -X subtree=<path in language worker repo> --squash <tag-name> --allow-unrelated-histories --strategy-option theirs`
        -   Example: `git merge -X subtree=src/WebJobs.Script.Grpc/azure-functions-language-worker-protobuf --squash v1.1.0-protofile --allow-unrelated-histories --strategy-option theirs`
4.	Finalize with commit
    -	`git commit -m "Updated subtree from https://github.com/azure/azure-functions-language-worker-protobuf. Tag: <tag-name>. Commit: <commit hash>"`
    -	`git push`

## Worker-delivered host.json configuration

`HostJsonConfiguration` carries a worker-local `host.json` snapshot in
`WorkerInitResponse.host_json` (field 6), separately from capabilities and worker
metadata. This is a draft protocol contract for
[APP-01](https://github.com/Azure/azure-functions-host/issues/12049), not a
production rollout. Compatible Host and worker implementations and maintainer
contract review are still required; other worker languages remain follow-ups.

### Delivery and ownership

Supporting workers always include `host_json` in `WorkerInitResponse` during
initialization. No capability request or Host feature flag gates delivery. Updated
workers read the local document once per process/application generation even
when the Host has not requested it. Older or co-located Hosts may ignore the
extra field.

An older or unsupported worker can omit the envelope. Absence means unsupported
or not delivered, not a missing file. In compute mode, the Host uses its legacy
configuration path rather than rejecting initialization solely because the
envelope is absent.

The worker owns the file and resolves `host.json` from its explicit local
application root. In compute mode it must not use the Host's
`WorkerInitRequest.function_app_directory`, which can identify a different
filesystem. An unavailable or invalid local application root produces
`ReadError`, not a fallback to the Host's path or the process working directory.
Proxies forward the envelope opaquely, without reading, decoding, or rewriting
its contents. The Host remains authoritative for text decoding, JSON parsing,
schema validation, and `host.json` version validation.
Delivery is not UTF-8-only: workers preserve bytes without decoding, including
any BOM. Host decoding preserves its local-file semantics (UTF-8 by default,
with BOM detection for UTF-8, UTF-16 LE/BE, and UTF-32 LE/BE) and rejects malformed
encoding without disclosing customer content or paths.

### Delivery outcomes

`HostJsonConfiguration` has a nested `Status` enum, `Status status = 1`, and
`bytes content = 2`.

| Status | Value | Meaning |
|---|---|---|
| `Unspecified` | 0 | No valid outcome was supplied; the Host must not treat this as a missing file. |
| `Available` | 1 | The entire file was read within the document cap; `content` is the original file bytes. |
| `Missing` | 2 | The worker has an application root, but `host.json` does not exist. |
| `ReadError` | 4 | The local application root is unavailable or invalid, or the file could not be read (for example, due to an access or I/O error). |
| `TooLarge` | 5 | The document exceeds the document cap; no partial document is sent. |

Enum number `3` and the old name `AppDirectoryUnavailable` are reserved and must
not be reused.

`Available` with empty `content` represents an empty file, not `Missing`; the Host
applies its normal parsing and validation rules to that empty file. A present
`Available` envelope must pass configuration validation; explicit `Missing` with
empty content allows the existing defaults. All non-`Available` statuses must
have empty `content`. `Unspecified`, unknown status values, and non-`Available`
outcomes carrying content are invalid deliveries.
`ReadError`, `TooLarge`, and invalid deliveries fail configuration acceptance;
they must never be treated as absence or trigger legacy-source fallback.
Read failures use these structured outcomes, not free-form exceptions or
customer JSON in `StatusResult`, capabilities, or worker metadata.

### Bounds and privacy

The document cap is **65,536 bytes (64 KiB), including any byte-order mark (BOM)**.
A file exactly at the cap is permitted. The worker must bound the actual read,
not just inspect a file-length value that can change before or during the read:
it can read up to the cap plus one detection byte to distinguish an oversized
file, then return `TooLarge` with no content. It must not read the entire file
and truncate it or allocate according to an untrusted file length.

The Host independently rejects delivered content above the cap before decoding
or parsing, even if a worker claims `Available`. This document cap is not the
overall gRPC message-size or process-memory budget: protobuf framing, other
fields, transport limits, allocations, and decoded/parsed representations have
their own bounds.

The document, path-bearing read exceptions, and arbitrary configuration values
must not appear in system telemetry, including logs, exceptions, capabilities,
or worker metadata. Diagnostics may describe the structured outcome without
including customer content or paths.

### Initialization and snapshot lifetime

A supporting worker captures one immutable outcome (status and any file bytes)
per process/application generation during initialization. It replays that
snapshot on same-generation initialization retries rather than rereading the
file, including when the captured outcome is a missing file or read failure.
There is no live reload.

`WorkerInitResponse.result` retains its process-initialization meaning. A
successful process initialization and acceptance of its configuration are
separate decisions; a configuration read failure does not rewrite an otherwise
successful process-initialization result.

In compute mode, the Host validates supplied envelopes and applies its
configuration acceptance rules before releasing first-link startup. The first
successfully admitted worker selects the configuration source: its supplied
snapshot (including explicit `Missing` defaults), or the legacy source when its
envelope is absent. Rejected or canceled candidates must not select a source.

That source choice stays fixed across later links and child-Host restarts. Later
supplied envelopes are still validated, but never replace the selected source
or its configuration, even when the first choice was the legacy source. Later
responses must not reset the startup gate. Errors and invalid deliveries cannot
bypass configuration acceptance by being treated as absent.

Comparing later snapshots is reserved for APP-02. This contract adds no hashes,
comparison or generation protocol, chunking, CLI switch, or
restart-on-configuration-change behavior.

## Consuming FunctionRPC.proto
*Note: Update versionNumber before running following commands*

## CSharp
```
set NUGET_PATH="%UserProfile%\.nuget\packages"
set GRPC_TOOLS_PATH=%NUGET_PATH%\grpc.tools\<versionNumber>\tools\windows_x86
set PROTO_PATH=.\azure-functions-language-worker-protobuf\src\proto
set PROTO=.\azure-functions-language-worker-protobuf\src\proto\FunctionRpc.proto
set PROTOBUF_TOOLS=%NUGET_PATH%\google.protobuf.tools\<versionNumber>\tools
set MSGDIR=.\Messages

if exist %MSGDIR% rmdir /s /q %MSGDIR%
mkdir %MSGDIR%

set OUTDIR=%MSGDIR%\DotNet
mkdir %OUTDIR%
%GRPC_TOOLS_PATH%\protoc.exe %PROTO% --csharp_out %OUTDIR% --grpc_out=%OUTDIR% --plugin=protoc-gen-grpc=%GRPC_TOOLS_PATH%\grpc_csharp_plugin.exe --proto_path=%PROTO_PATH% --proto_path=%PROTOBUF_TOOLS%
```
## JavaScript
In package.json, add to the build script the following commands to build .js files and to build .ts files. Use and install npm package `protobufjs`.

Generate JavaScript files:
```
pbjs -t json-module -w commonjs -o azure-functions-language-worker-protobuf/src/rpc.js azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto
```
Generate TypeScript files:
```
pbjs -t static-module azure-functions-language-worker-protobuf/src/proto/FunctionRpc.proto -o azure-functions-language-worker-protobuf/src/rpc_static.js && pbts -o azure-functions-language-worker-protobuf/src/rpc.d.ts azure-functions-language-worker-protobuf/src/rpc_static.js
```

## Java
Maven plugin : [protobuf-maven-plugin](https://www.xolstice.org/protobuf-maven-plugin/)
In pom.xml add following under configuration for this plugin
<protoSourceRoot>${basedir}/<path to this repo>/azure-functions-language-worker-protobuf/src/proto</protoSourceRoot>

## Python
```
python -m pip install -e .[dev] -U
python setup.py build
```

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

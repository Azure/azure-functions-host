# Compute separation with Aspire

A local development tool for running the real Functions Host, WorkerProxy, and .NET isolated `SampleIsolatedApp` together.

The tool supplies no Host-side `host.json`. By default, it sets the Host's existing read-only app-content option and uses local missing-file defaults. Worker-owned configuration delivery is an explicit opt-in until compatible workers are deployed.

## Prerequisites

- The repository's .NET SDK.
- For container deployment only, a running Linux x64 Docker engine with Docker Compose.

The first build downloads the matching Aspire toolchain into the worktree's build output. A global Aspire installation and Azure Functions Core Tools are not required.

Run the commands below from the repository root.

## Run with F5

Open `Azure.Functions.Host.slnx`, set `ComputeSeparation.AppHost` as the startup project, select a launch profile, and press **F5**:

| Profile | Runs locally |
| --- | --- |
| `project` | The Host, WorkerProxy, sample worker, and a fake platform as .NET projects for managed C# debugging. No Docker required. |
| `project-placeholder-manual` | Same as `project`, but the Host starts in placeholder mode, nothing is assigned or linked automatically, and ports are pinned for `compute-separation.http`. See [Placeholder mode](#placeholder-mode). |
| `container` | Three Linux containers: a ReadyToRun Host, Native AOT WorkerProxy, and ReadyToRun sample worker. |

`project` and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile container
```

Run one profile at a time. No mode requires `aspire deploy` or `aspire publish`.

## Worker-owned host.json (opt-in)

With a worker build that implements negotiated `HostJson` version `1` delivery, enable it in either project or container mode:

```powershell
$env:ComputeSeparation__EnableWorkerHostJson = "true"
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
```

AppHost forwards this setting as `AzureFunctionsWebHost__EnableWorkerHostJson=true` to the dedicated Host. Outside this harness, set that Host environment variable directly. It is root/bootstrap configuration, not a customer `host.json` setting, and defaults to `false`. Ordinary co-located execution does not register this feature. The existing sample's published worker package must be replaced with a compatible worker build before enabling it; an older worker will be rejected rather than silently using Host defaults. No worker command-line switch is required.

The Host requests capability `HostJson=1` during `WorkerInit`. The worker reads its own application file and returns the typed `WorkerInitResponse.host_json` envelope. The Host's synthetic script directory is not the worker's application directory. WorkerProxy only relays the envelope, including through capability/HTTP endpoint rewriting; it never reads, parses, defaults, or merges the file.

| Delivery | Host behavior when enabled |
| --- | --- |
| `Available` | Accept at most **65,536 raw bytes**, including any BOM. Decode strictly as UTF-8 by default or BOM-detected UTF-8, UTF-16 LE/BE, or UTF-32 LE/BE. Require a JSON object, supported host.json schema version (`2.0`), and supported configuration profile. |
| `Available` with empty bytes | Reject as an empty file, not as a missing file. An empty object `{}` retains existing empty-object normalization. |
| Explicit `Missing` with no bytes | Apply the existing missing-file defaults, including the normal read-only/app-kind/runtime rules, without creating a Host-side file. |
| Absent envelope | Reject as unsupported/not delivered, not as `Missing`. This includes old workers that ignore the requested capability. |
| `ReadError`, `AppDirectoryUnavailable`, `TooLarge`, unspecified/unknown status, or content contradicting a non-`Available` status | Reject the candidate with a bounded, content-free error. Invalid text encoding, JSON, version, and profile also reject it; no parser exception or document content is logged. |

This application-document cap is independent of gRPC message limits. Failed links remain retryable after cleanup. With the feature disabled, the Host neither requests nor applies the envelope and retains the legacy file path.

Rejected deliveries emit the `HostJsonConfigurationRejected` warning in the Host's `Azure.Functions.Rpc.Client.WorkerHostJsonConfiguration` log category. Its fixed, bounded reason distinguishes missing delivery, oversize, encoding, and document errors without logging worker values, paths, content, or parser exceptions. The public link response retains the existing generic `WorkerUnavailable` error.

These warnings use the existing platform system-log sink without changing customer logging filters. On Windows, capture `FunctionsSystemLogsEventSource` through an `EventListener`, ETW, or EventPipe: event `RaiseFunctionsEventWarning` (65522, Warning) carries `EventName=HostJsonConfigurationRejected`, `Source` for the category, and `Summary` for the safe message. The development root console's default category filter excludes `Azure.Functions.*`; customer `AzureFunctionsJobHost` console settings apply to the child ScriptHost and do not make these pre-start warnings appear on stdout.

Validation completes before initialized-channel publication and the first-link ScriptHost startup signal. The registry atomically admits the channel and retains the first accepted immutable configuration. ScriptHost installs a synchronous source at the original `host.json` position, before the extension-discovery configuration pre-build. Existing profile/default processing, `AzureFunctionsJobHost` key prefixing, unknown extension sections, arrays/scalars, and environment precedence are preserved. The source does not await RPC, write process environment variables, read worker files, or log even a sanitized copy of the document.

The snapshot survives worker loss and child ScriptHost restarts. Later workers must provide a valid delivery, but this draft **does not compare their documents**, replace the first configuration, or restart ScriptHost when a different valid document arrives. Multi-worker agreement is deferred to [APP-02 (#12048)](https://github.com/Azure/azure-functions-host/issues/12048). Initialized/configuration-accepted does not mean invocation-ready; channel setup remains a separate concern. There is no file watching or live configuration reload. Other language-worker deliveries and cross-repository completion remain tracked by [APP-01 (#12049)](https://github.com/Azure/azure-functions-host/issues/12049).

## Try the sample

Open the dashboard URL printed at startup. Find `functions-host` in project mode or `runtime` in container mode, open its `http` endpoint, and append `/api/hello`.

Once the Host has started, GET or POST requests return:

```text
Hello from the BYOC .NET isolated worker.
```

## Fake platform

WorkerProxy uses `WEBSITE_POD_NAME`, falling back to `CONTAINER_NAME`, for pod identity.
AppHost supplies `WEBSITE_POD_NAME` locally from `ComputeSeparation:PodName`.

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls in order. The fake is not wired in container mode.

## Endpoints

### Expected flow

The happy path, in order:

| Step | Endpoint on | Caller | Request | Purpose |
| --- | --- | --- | --- | --- |
| 1 | WorkerProxy | Platform | `PUT /admin/worker/assignment` | Assigns the pod once its worker has connected. |
| 2 | Host | Platform | `POST /admin/instance/assign` | Assigns the placeholder Host to the app, which then specializes. Not sent in `project`, whose Host starts already specialized. |
| 3 | Host | Platform | `PUT /admin/workers/{workerId}` | Links the worker through the WorkerProxy runtime gRPC endpoint. |
| 4 | Host | Client | `GET /api/hello` | Invokes the function through Host -> WorkerProxy -> worker. |

`GET /admin/worker/state` on the WorkerProxy reports the pod state at any time. The `project` profile sends steps 1 and 3 through the fake platform automatically; `project-placeholder-manual` leaves steps 1 to 3 to you.

### Test-only

Simulation routes live under `/simulate`, so they are never confused with the real contracts above.

| Endpoint on | Request | Effect |
| --- | --- | --- |
| Fake platform | `POST /simulate/worker/assign` | Sends step 1 to the WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/host/assign` | Sends step 2 to the Host and returns its response; see [Placeholder mode](#placeholder-mode). |
| Fake platform | `POST /simulate/worker/link` | Sends step 3 to the Host and returns its response. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-placeholder-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-placeholder-manual`, nothing is assigned or linked until you send the requests. Wait until every resource is **Running** in the dashboard, then send the numbered requests (1 to 4) in the order they appear in the file. If assign returns `503`, the sample worker has not connected yet; retry it before moving on. The requests marked *Optional* can be sent at any time.

## Placeholder mode

`project-placeholder-manual` runs the Host the way it starts before it is assigned to an app: in placeholder mode (`WEBSITE_PLACEHOLDER_MODE=1`), with placeholder content, until it is assigned. It exercises how the Host behaves when specialization and the worker link overlap.

AppHost generates a random key for each run and gives it to both the Host and the fake platform. The fake platform uses it to authenticate and encrypt its `POST /admin/instance/assign` request, as the Host expects. The Host answers `202`, leaves placeholder mode, and specializes on its next request. Specialization completes only after the first worker links, so if the Host holds the link until specialization completes, step 3 never returns and the Host's health check stops responding.

## Container behavior

The dashboard has two independent resources, each backed by its own Compose project:

| Resource | Owns |
| --- | --- |
| `runtime` | The Host container and the shared Docker network. |
| `worker-pod-1` | WorkerProxy and the sample worker, sharing a network namespace. |

Stopping `worker-pod-1` removes only that pod's containers, leaving the same Host and network running. Stopping `runtime` stops the Host but keeps the network until AppHost shutdown. A small adapter runs these Compose groups because Aspire's native containers do not support the Proxy/worker shared-network arrangement.

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Dynamic add/remove controls and invocation readiness for later-linked workers are not implemented yet.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

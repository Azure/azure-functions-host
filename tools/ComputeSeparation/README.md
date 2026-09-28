# Compute separation with Aspire

A local development tool for running the real Functions Host, WorkerProxy, and .NET isolated `SampleIsolatedApp` together.

The tool supplies no `host.json`. It sets the Host's existing read-only app-content option, so the unchanged Host uses defaults without writing that file.

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

`project` and `container` assign the worker pod to the `http` function group and then link the worker automatically. In `project`, AppHost makes both calls through the fake platform; in `container`, it calls the WorkerProxy and the Host directly. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile container
```

Run one profile at a time. No mode requires `aspire deploy` or `aspire publish`.

## Try the sample

Open the dashboard URL printed at startup. Find `functions-host` in project mode or `runtime` in container mode, open its `http` endpoint, and append `/api/hello`.

Once the Host has started, GET or POST requests return:

```text
Hello from the BYOC .NET isolated worker.
```

## Fake platform

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host. It also stands in for AppServer: the Host's `FUNCTIONS_APPSERVER_URI` points at it, so it receives the Host's total HTTP capacity.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls and the Host state pushes in order. The fake is not wired in container mode.

## Endpoints

### Expected flow

The happy path, in order:

| Step | Endpoint on | Caller | Request | Purpose |
| --- | --- | --- | --- | --- |
| 1 | WorkerProxy | Platform | `PUT /admin/worker/assignment` | Assigns the pod once its worker has connected. |
| 2 | Host | Platform | `POST /admin/instance/assign` | Assigns the placeholder Host to the app, which then specializes. Not sent in `project`, whose Host starts already specialized. |
| 3 | Host | Platform | `PUT /admin/workers/{workerId}` | Links the worker through the WorkerProxy runtime gRPC endpoint. |
| 4 | Host | Client | `GET /api/hello` | Invokes the function through Host -> WorkerProxy -> worker. |

After specialization, the Host publishes its current HTTP capacity with `PUT /admin/infra/host/state` on AppServer (the fake platform here). Capacity is zero until ScriptHost is running and an HTTP worker is invocation-ready. The first publish sends the current snapshot, so it may already be positive if worker setup finished first.

### HTTP capacity contract

```json
{ "snapshotVersion": 636374120, "httpCapacity": 16 }
```

`httpCapacity` is the sum of `MaxConcurrency` for initialized, invocation-ready workers whose `FunctionGroupName` is `http` (case-insensitive). Non-HTTP, unknown-group, and not-yet-ready workers contribute zero. The total is a 64-bit integer, allowing workers with different concurrency limits without overflowing a 32-bit sum. It is total capacity, not remaining capacity after active requests; no slot lease APIs or counters are involved.

The platform supplies optional `maxConcurrency` in `PUT /admin/worker/assignment`. An omitted or null value defaults to **16**; explicit values must be positive 32-bit integers. Concurrency is part of immutable assignment identity: an equivalent retry returns 204, but changing it returns 409. The worker state API reports the accepted value.

WorkerProxy stamps `FunctionGroupName` and `MaxConcurrency` into the first successful WorkerInitResponse, overriding worker-supplied values and freezing both for the session. The Host defaults to 16 only if `MaxConcurrency` is absent (for older proxies); an explicit malformed, zero, negative, or out-of-range capability fails initialization and the worker link.

The snapshot version advances only when total capacity changes. Capacity becomes zero when ScriptHost leaves Running or application shutdown starts. Failed publications retry the latest state, with backoff from 250 ms up to 30 seconds; an accepted version is not periodically resent. Outbound authentication remains deferred.

For example, ready HTTP workers advertising 8 and 32 contribute 40, regardless of any non-HTTP workers. In this one-worker harness, set a different concurrency before launching AppHost:

```powershell
$env:ComputeSeparation__MaxConcurrency = '32'
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
```

After assign, specialize, and link, `GET /simulate/host/state` should return `httpCapacity: 32`. The same setting controls the direct assignment in container mode, which has no fake state receiver. This changes advertised capacity; it does not introduce a new worker-side invocation throttle.

`GET /admin/worker/state` on the WorkerProxy reports the pod state at any time. The `project` profile sends steps 1 and 3 through the fake platform automatically; `project-placeholder-manual` leaves steps 1 to 3 to you.

### Test-only

Simulation routes live under `/simulate`, so they are never confused with the real contracts above.

| Endpoint on | Request | Effect |
| --- | --- | --- |
| Fake platform | `POST /simulate/worker/assign` | Sends step 1 to the WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/host/assign` | Sends step 2 to the Host and returns its response; see [Placeholder mode](#placeholder-mode). |
| Fake platform | `POST /simulate/worker/link` | Sends step 3 to the Host and returns its response. |
| Fake platform | `GET /simulate/host/state` | Returns the last Host state it accepted, or `204` if none yet. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-placeholder-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-placeholder-manual`, nothing is assigned or linked until you send the requests. Wait until every resource is **Running** in the dashboard, then send the numbered requests (1 to 4) in the order they appear in the file. If assign returns `503`, the sample worker has not connected yet; retry it before moving on. The requests marked *Optional* can be sent at any time.

## Placeholder mode

`project-placeholder-manual` runs the Host the way it starts before it is assigned to an app: in placeholder mode (`WEBSITE_PLACEHOLDER_MODE=1`), with placeholder content, until it is assigned. It exercises how the Host behaves when specialization and the worker link overlap.

AppHost generates a random key for each run and gives it to both the Host and the fake platform. The fake platform uses it to authenticate and encrypt its `POST /admin/instance/assign` request, as the Host expects. The Host answers `202`, leaves placeholder mode, and specializes on its next request. In compute separation, specialization applies configuration without starting or waiting for ScriptHost. The first worker link then activates ScriptHost; HTTP capacity remains zero until ScriptHost is running and the worker is ready.

## Container behavior

The dashboard has two independent resources, each backed by its own Compose project:

| Resource | Owns |
| --- | --- |
| `runtime` | The Host container and the shared Docker network. |
| `worker-pod-1` | WorkerProxy and the sample worker, sharing a network namespace. |

Stopping `worker-pod-1` removes only that pod's containers, leaving the same Host and network running. Stopping `runtime` stops the Host but keeps the network until AppHost shutdown. A small adapter runs these Compose groups because Aspire's native containers do not support the Proxy/worker shared-network arrangement.

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Dynamic add/remove controls are not implemented yet. The Host initializes later-linked workers for invocations and includes ready HTTP workers in its capacity.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

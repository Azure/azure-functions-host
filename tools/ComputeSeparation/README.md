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
| `project-manual` | Same as `project`, but nothing is assigned or linked automatically, and ports are pinned for `compute-separation.http`. |
| `container` | Three Linux containers: a ReadyToRun Host, Native AOT WorkerProxy, and ReadyToRun sample worker. |

`project` and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-manual
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

In project mode, the `fake-platform` resource is a local stand-in for the platform. It starts before the Host, sends the assign and link requests to the WorkerProxy and Host, and records the linked worker counts the Host publishes back. The Host receives its endpoint through `FUNCTIONS_APPSERVER_URI` and publishes with `PUT /admin/infra/host/state`:

```json
{ "snapshotVersion": 2, "linkedWorkerCount": 1, "linkedHttpWorkerCount": 1 }
```

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls and the Host's pushes in order. A push is marked `stale` and not accepted when its `snapshotVersion` is not greater than the accepted version. The fake is not wired in container mode.

## Endpoints

### Expected flow

The happy path, in order:

| Step | Endpoint on | Caller | Request | Purpose |
| --- | --- | --- | --- | --- |
| 1 | Platform | Host | `PUT /admin/infra/host/state` | Publishes counts `(0, 0)` after startup. |
| 2 | WorkerProxy | Platform | `PUT /admin/worker/assignment` | Assigns the pod once its worker has connected. |
| 3 | Host | Platform | `PUT /admin/workers/{workerId}` | Links the worker through the WorkerProxy runtime gRPC endpoint. |
| 4 | Platform | Host | `PUT /admin/infra/host/state` | Publishes counts `(1, 1)` once the linked worker is running. |
| 5 | Host | Client | `GET /api/hello` | Invokes the function through Host -> WorkerProxy -> worker. |

`GET /admin/worker/state` on the WorkerProxy reports the pod state at any time. The `project` profile sends steps 2 and 3 through the fake platform automatically; `project-manual` leaves them to you. Steps 1 and 4 require a Host that publishes its state.

### Test-only

Simulation and inspection routes live under `/simulate`, so they are never confused with the real contracts above. Only `PUT /admin/infra/host/state` on the fake platform is a real contract, because the Host calls it.

| Endpoint on | Request | Effect |
| --- | --- | --- |
| Fake platform | `POST /simulate/worker/assign` | Sends step 2 to the WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/worker/link` | Sends step 3 to the Host and returns its response. |
| Fake platform | `GET /simulate/host/state` | Returns the accepted state and every push received. |
| Fake platform | `DELETE /simulate/host/state` | Clears the history and accepted state. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-manual`, nothing is assigned or linked until you send the requests. Wait until every resource is **Running** in the dashboard, then send the numbered requests (1 to 5) in the order they appear in the file. If assign returns `503`, the sample worker has not connected yet; retry it before moving on. The requests marked *Optional* or *Test-only* can be sent at any time.

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

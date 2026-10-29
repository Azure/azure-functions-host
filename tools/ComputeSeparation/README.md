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

`project` and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

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

WorkerProxy uses `WEBSITE_POD_NAME`, falling back to `CONTAINER_NAME`, for pod identity.
AppHost supplies `WEBSITE_POD_NAME` locally from `ComputeSeparation:PodName`.

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls in order. The fake is not wired in container mode.

Each run generates three distinct synthetic keys: runtime container, WorkerProxy container and shared app. The runtime validates Link's `x-ms-site-token` with its own container key, including after specialization. WorkerProxy validates assignment and state-poll site tokens with its own key, even before a customer worker exists. That WorkerProxy key is also delivered to Host as `runtimeAccessKey` for data-plane caller JWTs. The app key is supplied through each process's normal configuration, not injected through WorkerProxy assignment. This local fixture does not establish deployed provisioning.

Link uses `PUT /admin/workers/{workerId}` with the WorkerProxy pod name in the path and the existing three-field body: `workerPodName`, `workerGrpcEndpoint`, `runtimeAccessKey`. The harness sends `workerGrpcEndpoint` as scheme-less host:port with the actual mapped port; Host supplies HTTPS for its outbound gRPC connection. Host sends one caller JWT. WorkerProxy returns its worker-bound app-key proof in `WorkerInitResponse.capabilities["WorkerProxyAppToken"]` before Host accepts Link.

HTTP routing remains negotiated through `HttpUri`; the harness sends the actual mapped HTTP authority as `httpProxyEndpoint` in assignment and WorkerProxy constructs HTTPS. It never guesses an external port or advertises the customer-local destination. AppHost also creates synthetic PEM files outside WorkerProxy, supplies their paths in project mode and mounts them read-only in container mode. Production images instead generate the PEM pair in their container-startup script. WorkerProxy never generates a replacement. The scoped runtime certificate policy checks expiry only. See [the runtime connection contract](../../src/Functions.WorkerProxy/README.md).

## Endpoints

### Expected flow

The happy path, in order:

| Step | Endpoint on | Caller | Request | Purpose |
| --- | --- | --- | --- | --- |
| 1 | WorkerProxy | Platform | `PUT /admin/worker/assignment` | Authenticates and records configuration without waiting for the customer worker. |
| 2 | Host | Platform | `POST /admin/instance/assign` | Assigns the placeholder Host to the app, which then specializes. Not sent in `project`, whose Host starts already specialized. |
| 3 | Host | Platform | `PUT /admin/workers/{workerId}` | Links the worker through the WorkerProxy runtime gRPC endpoint. |
| 4 | Host | Client | `GET /api/hello` | Invokes the function through Host -> WorkerProxy -> worker. |

WorkerProxy management availability is established with authenticated `POST /admin/infra/instanceState`, initially with an empty body. The next poll sends the last flat origin snapshot; no change returns 204. Separate readiness GET routes and `GET /admin/worker/state` do not exist. The local adapter polls with a WorkerProxy-key site token rather than an anonymous Aspire health check. The `project` profile sends steps 1 and 3 through the fake platform automatically; `project-placeholder-manual` leaves steps 1 to 3 to you.

The current auth-scoped state reports `Starting`/`Unlinked`, including after proof emission. The runtime identity/acceptance acknowledgement and config/content versions needed for complete platform `Running`/`Linked` publication are not delivered by this handshake. State polling is not a claim of serving convergence; the local function invocation is a separate check.

### Test-only

Simulation routes live under `/simulate`, so they are never confused with the real contracts above.

| Endpoint on | Request | Effect |
| --- | --- | --- |
| Fake platform | `POST /simulate/worker/assign` | Sends step 1 to the WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/host/assign` | Sends step 2 to the Host and returns its response; see [Placeholder mode](#placeholder-mode). |
| Fake platform | `POST /simulate/worker/link` | Sends step 3 to the Host and returns its response. |
| Fake platform | `POST /simulate/worker/state` | Authenticates an empty-body state poll and relays the snapshot or 204. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-placeholder-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-placeholder-manual`, nothing is assigned or linked until you send the requests. Wait until the management listeners are available, then send the numbered requests (1 to 4) in the order they appear in the file. Assignment need not wait for the sample worker; Link waits within its bounded initialization attempt. The requests marked *Optional* can be sent at any time.

## Placeholder mode

`project-placeholder-manual` runs the Host the way it starts before it is assigned to an app: in placeholder mode (`WEBSITE_PLACEHOLDER_MODE=1`), with placeholder content, until it is assigned. It exercises how the Host behaves when specialization and the worker link overlap.

AppHost gives the runtime's synthetic container key to the fake platform. The fake uses it to authenticate and encrypt `POST /admin/instance/assign` and supplies the shared app key in the normal specialization environment. The Host's `202` acknowledges assignment initiation, not completed configuration installation. Existing request buffering and specialization reload configuration before Link processing; compute separation does not wait for a placeholder ScriptHost restart. The ScriptHost starts after the first worker links.

## Container behavior

The dashboard has two independent resources, each backed by its own Compose project:

| Resource | Owns |
| --- | --- |
| `runtime` | The Host container and the shared Docker network. |
| `worker-pod-1` | WorkerProxy and the sample worker, sharing a network namespace. |

Stopping `worker-pod-1` removes only that pod's containers, leaving the same Host and network running. Stopping `runtime` stops the Host but keeps the network until AppHost shutdown. A small adapter runs these Compose groups because Aspire's native containers do not support the Proxy/worker shared-network arrangement.

The container profile assigns WorkerProxy through its mapped management endpoint before linking its HTTPS runtime endpoints, using the same authentication contract as project mode.

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Dynamic add/remove controls and invocation readiness for later-linked workers are not implemented yet.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

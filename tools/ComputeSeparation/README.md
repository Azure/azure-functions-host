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
| `project-identity` | The real Host, WorkerProxy, and worker plus a separate mock HTTP Token Service. The worker acquires a token before opening its worker connection; Host linking proceeds independently of assignment. |
| `project-placeholder-manual` | Same as `project`, but the Host starts in placeholder mode, nothing is assigned or linked automatically, and ports are pinned for `compute-separation.http`. See [Placeholder mode](#placeholder-mode). |
| `project-placeholder-manual-multiple-workers` | One placeholder Host, two WorkerProxies, two sample workers, and one fake platform. Assign and link each worker by name to test adding capacity after Host startup. |
| `container` | Three Linux containers: a ReadyToRun Host, Native AOT WorkerProxy, and ReadyToRun sample worker. |

`project`, `project-identity`, and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. The identity profile starts linking while assignment waits for the worker, avoiding a circular wait on startup token acquisition. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-identity
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual-multiple-workers
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile container
```

Run one profile at a time. No mode requires `aspire deploy` or `aspire publish`.

## Try the sample

Open the dashboard URL printed at startup. Find `functions-host` in project mode or `runtime` in container mode, open its `http` endpoint, and append `/api/hello`.

Once the Host has started, GET or POST requests return:

```text
Hello from the BYOC .NET isolated worker. Worker ID: aspire-<worker-guid>
```

Each worker gets an independently generated GUID-based ID, matching the identity used for its Host link.
Override the first with `ComputeSeparation:WorkerId` and the second with `ComputeSeparation:SecondWorker:WorkerId`;
neither ID is derived from the other. The proxy names remain `worker-proxy-1` and `worker-proxy-2`.

## Fake platform

WorkerProxy uses `WEBSITE_POD_NAME`, falling back to `CONTAINER_NAME`, for pod identity.
AppHost supplies `WEBSITE_POD_NAME` locally from `ComputeSeparation:PodName`.

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls in order. The fake is not wired in container mode.

One fake platform keeps an array of known worker proxies: `worker-proxy-1`, and `worker-proxy-2` in the multiple-worker
profile. Aspire supplies each entry's management address, runtime gRPC endpoint, and worker ID. Assign and link requests
select an entry by name; an unknown name returns `404` instead of choosing a different worker. Each worker has a distinct
worker ID, request ID, pod name, and proxy endpoints, and both link to the same Host.

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
| Fake platform | `POST /simulate/worker/{workerProxyName}/assign` | Sends step 1 to the named WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/host/assign` | Sends step 2 to the Host and returns its response; see [Placeholder mode](#placeholder-mode). |
| Fake platform | `POST /simulate/worker/{workerProxyName}/link` | Sends step 3 to the Host using the named proxy's worker ID and runtime gRPC endpoint. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-placeholder-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-placeholder-manual`, nothing is assigned or linked until you send the requests. Wait until every resource is **Running** in the dashboard, then send the numbered requests (1 to 4) in the order they appear in the file. If assign returns `503`, the sample worker has not connected yet; retry it before moving on. The requests marked *Optional* can be sent at any time.

### Add a second worker after startup

Select `project-placeholder-manual-multiple-workers`, then use the same `compute-separation.http` file:

1. Send steps 1 to 4 to assign the first pod, specialize the Host, link the first worker, and successfully invoke `/api/hello`.
2. Send steps 5 and 6 to assign and link the second worker. Do not assign or restart the Host again.
3. Repeat step 7 and compare the response's worker ID to see invocations served by both workers. You can also watch HTTP forwarding in the `worker-proxy-1` and `worker-proxy-2` console logs.

| Endpoint | `worker-proxy-1` | `worker-proxy-2` |
| --- | --- | --- |
| Fake platform | `http://localhost:7060` | Same platform |
| WorkerProxy management | `http://localhost:7081` | `http://localhost:7091` |
| Shared Host | `http://127.0.0.1:7071` | Same Host |

The runtime gRPC, worker gRPC, and proxy HTTP ports are allocated independently by Aspire. Nothing is assigned or linked
automatically in this profile, and both worker/proxy pairs are available for independent inspection in the dashboard.
Repeated link requests reuse the matching linked channel.

For example, both requests go to the same fake platform; only the selected proxy name changes:

```http
POST http://localhost:7060/simulate/worker/worker-proxy-1/link

###

POST http://localhost:7060/simulate/worker/worker-proxy-2/link
```

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

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Aspire does not currently create additional container pods automatically; use the multiple-worker project profile above for the manual late-link flow.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

## Opt-in identity proxy

The Host and WorkerProxy can forward managed-identity HTTP requests over the existing Host-initiated FunctionRpc stream. The Token Service stays in the Host environment; no Token Service sidecar or new cross-pod identity port is needed.

See [Token Service proxy configuration](../../src/Functions.WorkerProxy/README.md) for the required Host, WorkerProxy, and customer environment variables, configuration examples, limits, and failure behavior. The bridge is disabled by default; the `project-identity` profile below supplies its development-only settings automatically.

Host linking can proceed independently of WorkerProxy assignment; the manual harness's assign-before-link sequence is not a platform requirement. Customer-only restarts do not imply a new per-pod credential or a WorkerProxy restart.

### Debug the complete first-start flow

Run the `project-identity` profile or select it in Visual Studio. It is project-mode only; no Azure subscription, production credentials, or Docker is required.

The five processes are `mock-token-service`, `functions-host`, `worker-proxy-1`, `isolated-worker`, and `fake-platform`. AppHost generates distinct mock-upstream and per-pod credentials as secret parameters. Only the Host and mock service receive the upstream credential; only WorkerProxy and its customer receive the per-pod credential. The customer gets `IDENTITY_ENDPOINT`; WorkerProxy gets no endpoint variable and always binds its allocated identity port to loopback.

The full token path is:

```text
Mock Token Service <-- HTTP --> Host <-- existing gRPC --> WorkerProxy <-- HTTP --> worker SDK
```

Host's mock upstream is `/token`, while the worker uses `/msi/token`, exercising independent endpoint mapping. Before `builder.Build().RunAsync()`, the sample calls `ManagedIdentityCredential` and prints `Managed identity token acquired through WorkerProxy before worker startup.` without printing the token or either secret. Normal worker initialization follows; invoke `/api/hello` on the Host to confirm the application subsequently executes.

Set breakpoints in the sample's startup credential call, `IdentityProxyMiddleware`, `HttpProxyDuplexChannel`, `HostTokenServiceForwarder`, and the mock's `/token` handler to inspect each hop. The normal 30-second token and two-minute early-link deadlines still apply while stopped in a debugger.

Open the mock's `http` endpoint from the dashboard to use these development-only controls:

| Request | Purpose |
| --- | --- |
| `GET /simulate/state` | Inspect received/authorized request counts and current response controls, without secrets, tokens, or request queries. |
| `POST /simulate/response` | Set `delayMilliseconds` (0-120000), `statusCode` (200-599), `bodyBytes` (0-2097152), and `retryAfterSeconds` (0-3600). Omitted properties restore their defaults. |

For example, posting `{"statusCode":429,"retryAfterSeconds":7}` makes the next upstream request return 429 and `Retry-After: 7`; posting `{}` restores success. These controls do not bypass SDK token caching: to exercise another upstream request, restart the customer sample or make a raw authenticated local HTTP request. WorkerProxy itself never caches tokens. Mock tokens are deliberately invalid for Azure services.

The `Functions.Host.Tests` identity suite remains a faster automated socket/gRPC check. The interactive profile adds actual outbound HTTP and separate processes; neither substitutes for real Token Service interoperability, all-language qualification, or container network-isolation certification. Other existing profiles are unchanged and do not provision identity settings.

Worker restart, registration, assignment, and persistent pod-transport behavior remain owned by the existing WorkerProxy lifecycle. The registry-owned setup from [#12100](https://github.com/Azure/azure-functions-host/pull/12100) is integrated, but it does not change relay transport lifetime. This checkout still couples worker-stream termination to transport termination; the identity scenario is a first-start flow, not proof of preserved transport across customer-only restarts.

Enable production deployments only with the approved Host/WorkerProxy transport, authorization/revocation integration, and platform-provided settings. Do not expose the development transport or silently restore a worker-side Token Service as a fallback.

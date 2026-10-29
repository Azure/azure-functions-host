# Compute separation with Aspire

A local development tool for running the real Functions Host, WorkerProxy, and .NET isolated `SampleIsolatedApp` together.

The tool supplies no `host.json`. It sets the Host's existing read-only app-content option, so the unchanged Host uses defaults without writing that file.

## Prerequisites

- The repository's .NET SDK.
- A running Linux x64 Docker engine for Azurite in every launch profile; container mode also requires Docker Compose.

The first build downloads the matching Aspire toolchain into the worktree's build output. A global Aspire installation and Azure Functions Core Tools are not required.

Run the commands below from the repository root.

## Prepare matching worker packages

This feature uses matching local worker/Core/gRPC packages until the payload capability is released. Both project and container profiles
run the same `SampleIsolatedApp`, including its HTTP and queue functions. Build the local feed before the first launch:

```powershell
$hostRepo = (Get-Location).Path
$workerRepo = 'Q:\git\copilot-worktrees\azure-functions-dotnet-worker\YOUR-WORKTREE'
$feed = Join-Path $hostRepo 'out\extension-payload\packages'
$workerVersion = '2.52.0-extension-payload-local.4'
$sdkVersion = '2.1.0-extension-payload-local.2'
Push-Location $workerRepo
foreach ($project in @('DotNetWorker.Core', 'DotNetWorker.Grpc', 'DotNetWorker')) {
    dotnet pack "src\$project\$project.csproj" -c Release "-p:Version=$workerVersion" -o $feed
}
dotnet pack build\Sdk.slnf -c Release "-p:Version=$sdkVersion" -o $feed
Pop-Location

$config = Join-Path $workerRepo 'NuGet.config'
dotnet restore tools\ComputeSeparation\SampleIsolatedApp\SampleIsolatedApp.csproj `
    --configfile $config "-p:RestoreAdditionalProjectSources=$feed"
```

The sample's default package-version properties match these labels. Choose fresh labels when changing package contents and update those
defaults to match so project builds and the Docker source build use identical versions. The existing worker NuGet configuration is supplied
only to the local restore command because the Host repository's package-source mapping does not include this private feed. No configuration
files are edited and no packages are uploaded. The worker Docker build copies that feed and restores from it plus the repository's existing
public upstream, outside the Host repository's package-source-mapping configuration.

## Run with F5

Open `Azure.Functions.Host.slnx`, set `ComputeSeparation.AppHost` as the startup project, select a launch profile, and press **F5**:

| Profile | Runs locally |
| --- | --- |
| `project` | The Host, WorkerProxy, sample worker, and a fake platform as .NET projects for managed C# debugging, plus an Azurite container. |
| `project-placeholder-manual` | Same as `project`, but the Host starts in placeholder mode, nothing is assigned or linked automatically, and ports are pinned for `compute-separation.http`. See [Placeholder mode](#placeholder-mode). |
| `project-placeholder-manual-multiple-workers` | One placeholder Host, two WorkerProxies, two sample workers, and one fake platform. Assign and link each worker by name to test adding capacity after Host startup. |
| `container` | A ReadyToRun Host, Native AOT WorkerProxy, ReadyToRun sample worker, and Azurite in Linux containers. |

`project` and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual-multiple-workers
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile container
```

Run one profile at a time. No mode requires `aspire deploy` or `aspire publish`.

### Storage networking

Every profile starts one Azurite emulator. Project profiles use Aspire's storage resource and pass its loopback endpoints
to the Host and workers. Container mode starts Azurite inside the runtime Compose group, on the same private `compute`
network as the Host and WorkerProxy. The worker shares the Proxy's network namespace. These containers use
`azurite:10000` for blobs and `azurite:10001` for queues, without `host.docker.internal` or host-gateway mappings.

The runtime resource exposes `blob` and `queue` endpoints in the Aspire dashboard. Their dynamically allocated host
ports are published on loopback only; local tools and the acceptance verifier use these endpoints with the
`/devstoreaccount1` path and [Azurite's public development account](https://learn.microsoft.com/azure/storage/common/storage-use-azurite#well-known-storage-account-and-key).
Both address forms reach the same emulator. Functions Host starts after the emulator is healthy. Emulator data is
temporary, and the worker pod is removed before the runtime group and its network.

## Try the sample

Open the dashboard URL printed at startup. Find `functions-host` in project mode or `runtime` in container mode, open its `http` endpoint, and append `/api/hello`.

Once the Host has started, GET or POST requests return:

```text
Hello from the BYOC .NET isolated worker. Worker ID: aspire-<worker-guid>
```

Each worker gets an independently generated GUID-based ID, matching the identity used for its Host link.
Override the first with `ComputeSeparation:WorkerId` and the second with `ComputeSeparation:SecondWorker:WorkerId`;
neither ID is derived from the other. The proxy names remain `worker-proxy-1` and `worker-proxy-2`.

The same sample always includes `QueueEcho`. AppHost supplies the Azurite connection to the Host and every worker and selects
`sample-input` / `sample-output` queues for normal runs. Connect Azure Storage Explorer using the Aspire `queues` resource's connection
details in project mode, or the runtime's published storage endpoints in container mode as described above. Create
`sample-input` if needed and enqueue a Base64-encoded message; `sample-output` receives the returned text.

## Streamed-extension QueueTrigger acceptance

This local-only scenario uses the same `SampleIsolatedApp` and Compose-managed Azurite as normal container launches.
The Host and Proxy containers have no customer deployment mount. The worker advertises its extension ZIP;
the Host requests it over EventStream, extracts it, and loads the queue extension before starting listeners.
`WaitForFirstInitializedAsync` covers the worker handshake and extension-source preparation: advertised assemblies must be
received and extracted to disk before metadata acquisition or ScriptHost construction proceeds. The link HTTP response still
returns after the handshake, without waiting for the transfer. Initialization does not mean extension startups or listeners
have finished; those run during ScriptHost startup.
An actual input-queue message travels through Host -> WorkerProxy -> worker, and the Host writes the returned token
through the queue output binding. The verifier also calls `/api/hello` on that same deployment. An HTTP smoke test alone is not a substitute.

The worker produces a ZIP without an archive comment or ZIP64 end records. The Host uses the standard ZIP reader,
checks extraction stays within its staging directory, and retains basic limits: 256 MiB compressed, 1 GiB expanded,
1 MiB for `extensions.json`, 10,000 filesystem entries including implicit parents, and 32 path segments.
It does not add custom ZIP-directory/CRC validation or parse manifest types/hints during extraction; the existing
extension loader interprets the manifest. Additional archive hardening is deferred.
The .NET worker removes its SDK-owned metadata bootstrap startup and DLL/PDB only from the transferred ZIP;
the deployed manifest and files stay unchanged. The Host loads the resulting manifest without SDK-specific filtering.
The capability advertises worker support only. The Host explicitly requests the first accepted extension source,
serializing concurrent candidates and trying another after failure. Later links reuse that source without transferring
or comparing another archive. An advertising candidate has five minutes to receive and extract its source, starting
after it acquires the preparation gate; queued candidates do not spend their deadline while waiting. Expiration cancels
and drains receipt, removes partial files, and rejects the supplying link so another candidate can proceed. It does not
silently select Host-local extensions. Closing the link or shutting down also cancels any in-flight transfer; there is no
separate payload-cancellation message.
Payload request/response coordination lives in `RpcClientWorkerChannel.cs`, with ordered writes and draining handled by
`ExtensionPayloadReceiver`. The root-owned payload manager retains the extracted source after the supplying worker disconnects.
Extension consumers resolve metadata/probing paths through `IExtensionPathProvider`. Client composition replaces the
default application-layout provider; the startup locator itself has no payload-specific path selection.
The transferred manifest is expected to contain correct assembly hints; the locator's existing assembly lookup is unchanged.
Durable trigger sync keeps its existing bundle and legacy-version logic, using the provider only when an alternate extension source is selected.

After preparing the local packages above, run:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj `
    --launch-profile container -- --ComputeSeparation:VerifyQueueTrigger=true
```

Verification only selects unique queues and runs the checks; it does not switch sample projects, worker build contexts or deployment layout.
The sample treats package-version fallback as an error and uses the unchanged SDK's default bootstrap output.
Its local SDK-bundled metadata generator is used if a development analyzer package omitted that analyzer.

The verifier creates unique input/output queues, enqueues a correlation token, checks the returned output and successful Host invocation,
and captures the Host's existing load-context/extension-startup diagnostics. Proxy logs are saved for diagnostics without per-invocation
forwarding logs. The existing WorkerProxy relay is unchanged, including its unbounded queues. The verifier also checks that the
deployment is worker-only, its original bootstrap remains present, and the transferred manifest/DLL/PDB exclude that bootstrap.
A successful run writes `result.json`, both original/filtered manifests, container logs and mount evidence beneath
`out\extension-payload\evidence\<input-queue>`, prints
`QueueTrigger acceptance succeeded`, and stops its resources. Missing tools, transfer/validation/startup failures, or an
early stop do not satisfy acceptance. Only this run's resources are cleaned up; nothing is deployed remotely.

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

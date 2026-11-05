# Compute separation with Aspire

A local development tool for running the real Functions Host, WorkerProxy, and .NET isolated `SampleIsolatedApp` together.

The tool supplies no Host-side `host.json` and sets the Host's existing read-only app-content option. The compute Host always requests worker delivery with the `HostConfiguration=1` initialization capability. If the first admitted worker sends no envelope, it selects file-based loading, which uses local missing-file defaults in this tool.

## Prerequisites

- The repository's .NET SDK.
- A running Linux x64 Docker engine for Azurite in every launch profile; container mode also requires Docker Compose.

The first build downloads the matching Aspire toolchain into the worktree's build output. A global Aspire installation and Azure Functions Core Tools are not required.

Run the commands below from the repository root.

## Prepare matching worker packages

This feature uses matching local worker/Core/gRPC packages until the payload capability is released. Both project and container profiles
run the same `SampleIsolatedApp`, including its HTTP and queue functions. Build the local feed before the first launch:

The integrated protocol retains `StreamingMessage.http_proxy` at field **35**. Matching worker packages must use
`get_extension_payload_request` at **36** and `extension_payload_response` at **37**; packages built against the
original extension-only 35/36 schema are not wire-compatible with this integration.

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
| `project-identity` | The real Host, WorkerProxy, and worker plus a separate mock HTTP Token Service. Authenticated assignment precedes Link; the worker acquires a token before opening its worker connection. |
| `project-placeholder-manual` | Same as `project`, but the Host starts in placeholder mode, nothing is assigned or linked automatically, and ports are pinned for `compute-separation.http`. See [Placeholder mode](#placeholder-mode). |
| `project-placeholder-manual-multiple-workers` | One placeholder Host, two WorkerProxies, two sample workers, and one fake platform. Assign and link each worker by name to test adding capacity after Host startup. |
| `container` | A ReadyToRun Host, Native AOT WorkerProxy, ReadyToRun sample worker, and Azurite in Linux containers. |

`project`, `project-identity`, and `container` link the worker automatically. AppHost authenticates and records assignment before linking; neither assignment nor identity bootstrap requires the customer worker to connect first. The identity profile can therefore acquire its startup token over the pending Link without a circular startup wait. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-identity
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-placeholder-manual-multiple-workers
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile container
```

Run one profile at a time. No mode requires `aspire deploy` or `aspire publish`.

## Host configuration delivery

Workers deliver an immutable Host Configuration JSON payload, which may be generated entirely in memory. The compute/BYOC Host always advertises `WorkerInitRequest.capabilities["HostConfiguration"] = "1"` in both project and container modes. Supporting workers check for that exact capability value before obtaining or preparing configuration. No Host feature flag, configuration setting, or worker command-line switch is required. The existing sample's published worker package must be replaced with a compatible worker build to deliver Host configuration. An older worker can still start using file-based loading. Ordinary co-located Host execution does not advertise this capability and continues to use file-based loading.

The worker returns the typed `WorkerInitResponse.host_configuration` envelope. Its acquisition source is not part of the delivery contract. WorkerProxy forwards the initialization request unchanged and does not add the capability. It only relays the returned envelope, including through capability/HTTP endpoint rewriting; it never acquires, parses, defaults, or merges configuration.

| Delivery | Compute Host behavior |
| --- | --- |
| `Available` | Accept at most **65,536 transmitted UTF-8 bytes**, without a BOM. Require a strict JSON object and supported configuration profile. No file-schema version header is required. |
| `Available` with empty bytes | Reject as invalid JSON, not as missing configuration. An empty object `{}` is valid empty configuration. |
| Explicit `Missing` with no bytes | Delivery is supported but no configuration is available. Select empty delivered configuration with the applicable profile, not file-based fallback. |
| Absent envelope | If this is the first admitted worker, select file-based loading. This is not explicit `Missing` and emits no rejection warning. Later absence leaves the selected source unchanged. |
| `Error`, `TooLarge`, unspecified/unknown status, or content contradicting a non-`Available` status | Reject the candidate with a bounded, content-free error. Invalid UTF-8, a BOM, invalid JSON, and unsupported profiles also reject it; no parser exception or payload content is logged. |

`Error` means the worker could not obtain, generate, or convert Host configuration. Workers own source-encoding conversion to BOM-free UTF-8 and bound acquisition/conversion; the Host independently validates the final payload. The limit applies after conversion, not to the source representation.

Delivered configuration rejects non-JSON syntax, including `undefined`, unquoted `NaN`/`Infinity`/`-Infinity`, single-quoted or unquoted property names, comments, and trailing commas. Ordinary file-based `host.json` parsing retains its existing permissive behavior. Valid delivered JSON still uses the shared profile and value-conversion behavior.

This payload cap is independent of gRPC message limits. Failed links remain retryable after cleanup. A present failure or invalid payload never falls back to file-based loading.

Rejected deliveries emit the `HostConfigurationRejected` warning in the Host's `Azure.Functions.Rpc.Client.WorkerHostConfigurationParser` log category. Its fixed, bounded reason distinguishes production, status, size, encoding, and payload errors without logging configuration values, paths, content, or parser exceptions. The parser throws a sanitized `HostConfigurationException`; the registry translates that failure to the existing `WorkerLinkException` admission contract. The public link response remains HTTP 503 with the generic `WorkerUnavailable` error.

These warnings use the existing platform system-log sink without changing customer logging filters. On Windows, capture `FunctionsSystemLogsEventSource` through an `EventListener`, ETW, or EventPipe: event `RaiseFunctionsEventWarning` (65522, Warning) carries `EventName=HostConfigurationRejected`, `Source` for the category, and `Summary` for the safe message. The development root console's default category filter excludes `Azure.Functions.*`; customer `AzureFunctionsJobHost` console settings apply to the child ScriptHost and do not make these pre-start warnings appear on stdout.

Validation completes before initialized-channel publication and the first-link ScriptHost startup signal. The first successful worker admission atomically selects either the delivered snapshot or file-based loading. Rejected or canceled candidates select neither; requesting a source before admission fails instead of choosing file-based loading early. ScriptHost installs a snapshot at the same configuration precedence as file-based loading, before the extension-discovery configuration pre-build. Existing profile processing, `AzureFunctionsJobHost` key prefixing, unknown extension sections, arrays/scalars, and environment precedence are preserved. Delivered settings do not infer extension bundles, enable PowerShell managed dependencies, or synthesize file-bootstrap version/default markers. Applications that need a bundle must supply its configuration explicitly. File-based loading retains its existing schema validation, defaults, logging, and read-only behavior. Snapshot loading is synchronous and does not await RPC, write process environment variables, perform file I/O, or log even a sanitized copy of the payload.

The source choice survives worker loss and child ScriptHost restarts, including when file-based loading was selected. Later supplied envelopes are still validated, but never replace that choice or a selected snapshot. Later workers may omit the envelope without switching a selected snapshot to file-based loading. This draft **does not compare payloads** or restart ScriptHost for a different valid delivery. Multi-worker agreement is deferred to [APP-02 (#12048)](https://github.com/Azure/azure-functions-host/issues/12048). Initialized/configuration-accepted does not mean invocation-ready; channel setup remains a separate concern. No live reload is added. Other language-worker deliveries and cross-repository completion remain tracked by [APP-01 (#12049)](https://github.com/Azure/azure-functions-host/issues/12049).
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

Each local worker gets an independently generated GUID-based StartStream ID. Host Link uses its protected WorkerProxy pod name instead.
Override the first with `ComputeSeparation:WorkerId` and the second with `ComputeSeparation:SecondWorker:WorkerId`;
neither ID is derived from the other. The proxy names remain `worker-proxy-1` and `worker-proxy-2`.

## System telemetry

WorkerProxy emits only its own platform records, separately from ordinary ASP.NET Core/YARP console output.
Worker `RpcLog` messages are forwarded unchanged, not processed by proxy telemetry.
The local OpenTelemetry log exporter writes through the unchanged shared `BufferedConsoleWriter`
to stdout; there is no OTLP exporter. Disposal uses the writer's existing best-effort drain
(up to five seconds). Explicit `ForceFlush` is unsupported and returns `false`.

Records use the existing `MS_FUNCTIONS_WORKER_POD_LOGS` prefix and 21-column positional format.
Worker/session IDs (when known) and operation details are included in the message; the InvocationId column is empty.
The shared log context contains only `WorkerId` and `ProxySessionId`. Event-specific details such as
relay side, protocol stage/status, and assignment status/duration are written directly in the log message,
not duplicated in context attributes.
Assignment 5xx responses and thrown failures emit `WorkerAssignmentRequestFailed`; validation failures
remain `WorkerAssignmentRequestRejected`. Exceptions are rethrown, and assignment failure records include
only the exception type. When a thrown failure has no known HTTP status, the log reports `unavailable`
rather than the response's default status.
An `OperationCanceledException` on an aborted assignment request instead emits
`WorkerAssignmentRequestCanceled` at Information and is rethrown. Unrelated failures remain errors.
Single and batched function loads log received/forwarded boundaries. Batch records contain counts,
not per-function records or worker exception text; unsuccessful or missing result statuses produce a Warning.
Expected Host shutdown during linking is Information/`Canceled`; initialization timeouts remain Warning/`Failed`.
The platform uses the protected WorkerProxy pod name for `PUT /admin/workers/{workerId}`.
The local worker's separate `StartStream` identity remains available for diagnostic correlation.
Scope correlation to the app/deployment and proxy instance; session IDs are local to one process.
Per-request HTTP and per-invocation RPC platform logs are not emitted. Existing HTTP activity instrumentation is unchanged.

Fields are bounded and encoded without splitting valid UTF-16 surrogate pairs; unsafe identities are fingerprinted rather than truncated.
Proxy diagnostic text is not sanitized. Payloads, assignment configuration and worker-authored logs
are excluded.
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

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host, and receives Host state through a prototype AppServer endpoint.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls in order. The fake is not wired in container mode.

Each run generates three distinct synthetic keys: runtime container, WorkerProxy container and shared app. The runtime validates Link's `x-ms-site-token` with its own container key, including after specialization. WorkerProxy validates assignment and state-poll site tokens with its own key, even before a customer worker exists. That WorkerProxy key is also delivered to Host as `runtimeAccessKey` for data-plane caller JWTs. The app key is supplied through each process's normal configuration, not injected through WorkerProxy assignment. This local fixture does not establish deployed provisioning.

Both receivers validate their own container key and pod identity at startup; invalid server configuration fails named options validation rather than returning a caller 401. Missing or invalid caller tokens under valid server configuration still return 401/Unauthenticated. The runtime's application proof key is read from live specialization configuration only when validating a reverse proof, so absent or invalid application-key configuration does not block management startup or caller-token signing.

Assignment simulations send requested config/content Unix epoch seconds as integer numbers (synthetic zero versions for this harness) and an effective HTTP `maxHttpConcurrency` of 16. Requested versions participate in retry identity but do not confirm loaded configuration or content; their nullable numeric-string compatibility and exact Int64 precision are covered by the endpoint/native fixtures. HTTP concurrency is a separate startup-capacity contract: omission defaults to 16, explicit integers must be from 1 through 1000, and explicit null or numeric strings are invalid. Non-HTTP groups accept integers from 0 through 1000 without a default. Equivalent effective values share retry identity; invalid HTTP zero is never coerced to the default.

Link uses `PUT /admin/workers/{workerId}` with the WorkerProxy pod name in the path and the existing three-field body: `workerPodName`, `workerGrpcEndpoint`, `runtimeAccessKey`. The harness sends `workerGrpcEndpoint` as scheme-less host:port with the actual mapped port; Host supplies HTTPS for its outbound gRPC connection. Host sends one caller JWT. WorkerProxy returns its worker-bound app-key proof in `WorkerInitResponse.capabilities["WorkerProxyAppToken"]` before Host accepts Link.

HTTP routing remains negotiated through `HttpUri`; the harness sends the actual mapped HTTP authority as `httpProxyEndpoint` in assignment and WorkerProxy constructs HTTPS. It never guesses an external port or advertises the customer-local destination. AppHost also creates synthetic PEM files outside WorkerProxy, supplies their paths in project mode and mounts them read-only in container mode. Production images instead generate the PEM pair in their container-startup script. WorkerProxy never generates a replacement. The scoped runtime certificate policy checks expiry only. See [the runtime connection contract](../../src/Functions.WorkerProxy/README.md).
One fake platform keeps an array of known worker proxies: `worker-proxy-1`, and `worker-proxy-2` in the multiple-worker
profile. Aspire supplies each entry's management address, mapped HTTPS authority, runtime gRPC endpoint, per-proxy container key, and protected pod identity.
The assignment sends that scheme-less HTTP authority, including default ports, and publishes it as the effective
HTTPS runtime origin. No proxy origin is configured at startup. Assign and link requests select an entry by name; an unknown name returns
`404` instead of choosing a different worker. Each worker has a distinct worker ID, request ID, pod name, and proxy
endpoints, and both link to the same Host.

`httpProxyEndpoint` is optional for non-HTTP workers. HTTP-capable workers require it in the accepted assignment
before successful HTTP initialization; supply a scheme-less mapped DNS name, IPv4 address or bracketed IPv6 literal with an explicit nonzero port,
such as `worker-proxy-1:28080` or `[::1]:28080`. Compatible absolute HTTPS origins are also accepted; scoped IPv6 and plaintext origins are rejected.
There is no startup fallback: `WorkerProxy:HttpProxyEndpoint` and `WORKERPROXY__HTTPPROXYENDPOINT` are no longer consumed.
An accepted assignment without the field leaves the effective origin unset, so HTTP capability initialization fails
rather than advertising the worker's local address. The effective worker destination must have a different origin
from the mapped proxy endpoint to prevent a forwarding loop.

Roll out assignment producers together with this WorkerProxy version: each platform must send the mapped authority
for HTTP workers before initialization completes. Older producers that rely on the startup setting are not compatible with this
HTTP initialization flow. The harness authenticates and records assignment before linking, including in `project-identity`;
assignment does not require the customer to start, so pending Link can carry startup token acquisition before `StartStream`.
The relay also holds a successful HTTP initialization response until assignment is accepted without blocking runtime
token traffic or worker EOF detection. Messages behind initialization remain ordered; worker request-stream completion
terminates the session and cancels that wait, even without call cancellation.
Placeholder assignment tokens use the configured Host container name as their audience, matching Host placeholder authentication.

## Host state publication (prototype)

AppHost sets the project's Host `MESH_INIT_URI` to the fake platform. After specialization, the Host sends
`PUT /admin/infra/host/state` with this JSON shape:

```json
{
  "createdTime": "2026-10-02T12:34:56.123+00:00",
  "httpCapacity": 16,
  "linkedWorkerCount": 1
}
```

Each publication also sends `fx-current-instance` from the Host's current `CONTAINER_NAME` (`aspire-runtime` in the
local project and container profiles). A missing, empty, or whitespace-only Host identity fails before sending; there
is no worker-pod identity fallback. The project-mode fake AppServer checks that value and returns HTTP 401 when it
is missing or does not match the configured Host identity. This exercises the internal instance-identity convention
for development; it is not full TLS or token authentication.

`httpCapacity` is the total of registry-ready HTTP workers' assigned concurrency limits, not free slots after active
requests or Frontend reservations. This sample explicitly assigns `maxHttpConcurrency: 16` to each HTTP worker, so one
ready worker contributes 16 and two contribute 32; the Host does not assume equal limits or multiply worker count by 16.
`linkedWorkerCount` includes initialized linked workers even before they are ready and includes non-HTTP workers.
Publication waits for specialization, not for a ScriptHost or a positive capacity.

Use `GET /simulate/host/state` to inspect the latest received snapshot; `204` means none has arrived. The fake records
arrival order without dropping timestamps that move backward. Timestamps are not versions and can be equal across
distinct snapshots. Repeated matching worker links do not create another snapshot.

Recoverable request failures retry the latest snapshot through a DI-registered Polly policy with exponential backoff
from 250 milliseconds to 30 seconds; a new snapshot interrupts that wait. `Microsoft.Extensions.Http.Polly` supplies
the policy-registry integration. The named HTTP client bounds each request to five seconds and does not retry serialized
requests. Each publication retry reads the latest runtime snapshot. After a successful HTTP response, the same snapshot is
not periodically resent. After specialization, normal root Host shutdown withdraws capacity and makes one best-effort final
attempt bounded to two seconds. An unexpected publisher failure is logged without attempting another publication; fatal
exceptions are not swallowed. This does not guarantee delivery or establish an AppServer admission policy.

HTTP 401/403 responses are logged as authentication rejections, not transient failures. The rejected snapshot is not sent
again on a retry timer or unchanged shutdown; publication resumes only when runtime state changes. The Host keeps running.

The receiver and wire shape are a local prototype, not verification of a deployed AppServer contract. The local
identity check does not provide full TLS/token authentication or an applied-version acknowledgement, and an AppServer
restart is not repaired without a later Host state change. The client reads `CONTAINER_NAME` per request, independently
of endpoint resolution. It resolves and caches `MESH_INIT_URI` on its first publication after specialization, defaulting
to `http://localhost:6060/` when unset; later configuration changes do not replace that endpoint for the root Host's
lifetime. Container mode has no fake AppServer receiver; its Host will log publication failures and retry the default
endpoint without preventing the worker link or invocation.
Host state publication does not establish WorkerProxy `Running`/`Linked` state or confirmed loaded versions.

## Endpoints

### Expected flow

The happy path, in order:

| Step | Endpoint on | Caller | Request | Purpose |
| --- | --- | --- | --- | --- |
| 1 | WorkerProxy | Platform | `PUT /admin/worker/assignment` | Authenticates and records configuration without waiting for the customer worker. |
| 2 | Host | Platform | `POST /admin/instance/assign` | Assigns the placeholder Host to the app, which then specializes. Not sent in `project`, whose Host starts already specialized. |
| 3 | Host | Platform | `PUT /admin/workers/{workerId}` | Links the worker through the WorkerProxy runtime gRPC endpoint. |
| 4 | Host | Client | `GET /api/hello` | Invokes the function through Host -> WorkerProxy -> worker. |

WorkerProxy management availability is established with one authenticated `POST /admin/infra/instanceState`, initially with an empty body or JSON `null`. It returns HTTP 200 with a complete typed startup checkpoint, including the actual pod name, startup time, and revision before assignment. Platform/app identity fields use explicit `unknown` placeholders and confirmed-version fields remain zero; neither establishes a trusted application identity, a loaded version, or a matched app cohort. The next poll sends the complete last checkpoint; an unchanged checkpoint long-polls and returns 204 at its deadline. Stale revision/process hints and valid extra metadata never install identity or acknowledge a runtime. Separate readiness GET routes and `GET /admin/worker/state` do not exist. The local adapter polls with a WorkerProxy-key site token rather than an anonymous Aspire health check. The `project` profile sends steps 1 and 3 through the fake platform automatically; `project-placeholder-manual` leaves steps 1 to 3 to you.

The current auth-scoped WorkerProxy state reports `Starting`/`Unlinked`, including after proof emission. Requested version metadata is retained for assignment retry identity, not proof of applied versions. Effective HTTP concurrency is advertised from the accepted assignment, captured after successful authenticated initialization, and summed only for registry-ready HTTP workers; runtime shutdown withdraws that capacity. Runtime identity/acceptance acknowledgement and confirmed loaded versions needed for complete platform `Running`/`Linked` publication remain a separate follow-up. State polling and runtime capacity do not claim full serving convergence; the local function invocation is a separate check.

### Test-only

Simulation routes live under `/simulate`, so they are never confused with the real contracts above.

| Endpoint on | Request | Effect |
| --- | --- | --- |
| Fake platform | `POST /simulate/worker/{workerProxyName}/assign` | Sends step 1 to the named WorkerProxy and returns its response. |
| Fake platform | `POST /simulate/host/assign` | Sends step 2 to the Host and returns its response; see [Placeholder mode](#placeholder-mode). |
| Fake platform | `POST /simulate/worker/{workerProxyName}/link` | Sends step 3 to the Host using the named proxy's worker ID and runtime gRPC endpoint. |
| Fake platform | `GET /simulate/host/state` | Reads the latest prototype Host state publication, or returns `204` before one arrives. |

## Send requests manually

`compute-separation.http` walks through the expected flow one step at a time by calling the fake platform, which sends the assign and link calls. Run the `project-placeholder-manual` profile, then open the file in Visual Studio or VS Code with the REST Client extension.

> [!NOTE]
> In `project-placeholder-manual`, nothing is assigned or linked until you send the requests. Wait until the management listeners are available, then send the numbered requests (1 to 4) in the order they appear in the file. Assignment need not wait for the sample worker; Link waits within its bounded initialization attempt. The requests marked *Optional* can be sent at any time.

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

AppHost gives the runtime's synthetic container key to the fake platform. The fake uses it to authenticate and encrypt `POST /admin/instance/assign` and supplies the shared app key in the normal specialization environment. The Host's `202` acknowledges assignment initiation, not completed configuration installation. Existing request buffering and specialization reload configuration before Link processing; compute separation does not wait for a placeholder ScriptHost restart. The ScriptHost starts after the first worker links.

## Container behavior

The dashboard has two independent resources, each backed by its own Compose project:

| Resource | Owns |
| --- | --- |
| `runtime` | The Host container and the shared Docker network. |
| `worker-pod-1` | WorkerProxy and the sample worker, sharing a network namespace. |

Stopping `worker-pod-1` removes only that pod's containers, leaving the same Host and network running. Stopping `runtime` stops the Host but keeps the network until AppHost shutdown. A small adapter runs these Compose groups because Aspire's native containers do not support the Proxy/worker shared-network arrangement.

The container profile assigns WorkerProxy through its mapped management endpoint before linking its HTTPS runtime endpoints, using the same authentication contract as project mode.

The WorkerProxy Dockerfile uses the repository root as its build context. Its sibling `Dockerfile.dockerignore` explicitly includes the product version props, shared management handler, RPC authentication helper, Token Service proxy sources, tracked writer, narrowly source-linked `BufferedConsoleWriter.cs`, and protobuf inputs compiled by `Functions.WorkerProxy.csproj`. Keep these rules aligned when adding linked inputs; reopening ancestor directories must not admit unrelated source trees. Build outputs, local settings and environment files remain excluded.

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, pod name (`WORKER_POD_NAME`, passed to WorkerProxy as `WEBSITE_POD_NAME`), worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Aspire does not currently create additional container pods automatically; use the multiple-worker project profile above for the manual late-link flow.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

## Opt-in Token Service proxy

The Host and WorkerProxy can forward managed-identity HTTP requests over the existing Host-initiated FunctionRpc stream. The Token Service stays in the Host environment; no Token Service sidecar or new cross-pod identity port is needed.

See [Token Service proxy configuration](../../src/Functions.WorkerProxy/README.md) for the required Host, WorkerProxy, and customer environment variables, configuration examples, limits, and failure behavior. The bridge is disabled by default; the `project-identity` profile below supplies its development-only settings automatically.

The harness authenticates and records WorkerProxy assignment before opening Link. Assignment does not require the customer to start, so the identity scenario can establish its authenticated transport and token bootstrap before customer `StartStream` without racing runtime admission against assignment. Customer-only restart recovery is not demonstrated by this first-start flow.

### Debug the complete first-start flow

Run the `project-identity` profile or select it in Visual Studio. It is project-mode only; no Azure subscription, production credentials, or Docker is required.

The five processes are `mock-token-service`, `functions-host`, `worker-proxy-1`, `isolated-worker`, and `fake-platform`. AppHost generates distinct mock-upstream and per-pod credentials as secret parameters. Only the Host and mock service receive the upstream credential; only WorkerProxy and its customer receive the per-pod credential. The customer gets `IDENTITY_ENDPOINT`; WorkerProxy gets no endpoint variable and always binds its allocated Token Service proxy port to loopback.

The full token path is:

```text
Mock Token Service <-- HTTP --> Host <-- existing gRPC --> WorkerProxy <-- HTTP --> worker SDK
```

Host's mock upstream is `/token`, while the worker uses `/msi/token`, exercising independent endpoint mapping. Before `builder.Build().RunAsync()`, the sample calls `ManagedIdentityCredential` and prints `Managed identity token acquired through WorkerProxy before worker startup.` without printing the token or either secret. Normal worker initialization follows; invoke `/api/hello` on the Host to confirm the application subsequently executes.

Set breakpoints in the sample's startup credential call, `TokenServiceProxyMiddleware`, `TokenServiceProxyDuplexChannel`, `HostTokenServiceForwarder`, and the mock's `/token` handler to inspect each hop. The normal 30-second token and two-minute early-link deadlines still apply while stopped in a debugger.

Open the mock's `http` endpoint from the dashboard to use these development-only controls:

| Request | Purpose |
| --- | --- |
| `GET /simulate/state` | Inspect received/authorized request counts and current response controls, without secrets, tokens, or request queries. |
| `POST /simulate/response` | Set `delayMilliseconds` (0-120000), `statusCode` (200-599), `bodyBytes` (0-2097152), and `retryAfterSeconds` (0-3600). Omitted properties restore their defaults. |

For example, posting `{"statusCode":429,"retryAfterSeconds":7}` makes the next upstream request return 429 and `Retry-After: 7`; posting `{}` restores success. These controls do not bypass SDK token caching: to exercise another upstream request, restart the customer sample or make a raw authenticated local HTTP request. WorkerProxy itself never caches tokens. Mock tokens are deliberately invalid for Azure services.

The existing `Functions.Host.Tests` identity suite uses normal startup keys, supplied PEMs and authenticated assignment with its controlled HTTP upstream. The interactive profile adds separate processes and the sample's actual startup call; neither substitutes for real Token Service interoperability, all-language qualification, or container network-isolation certification. Other existing profiles are unchanged and do not provision identity settings.

Worker restart, registration, assignment, and persistent pod-transport behavior remain owned by the existing WorkerProxy lifecycle. The registry-owned setup from [#12100](https://github.com/Azure/azure-functions-host/pull/12100) is integrated, but it does not change relay transport lifetime. This checkout still couples worker-stream termination to transport termination; the identity scenario is a first-start flow, not proof of preserved transport across customer-only restarts.

Enable production deployments only with the approved Host/WorkerProxy transport, authorization/revocation integration, and platform-provided settings. Do not expose the development transport or silently restore a worker-side Token Service as a fallback.

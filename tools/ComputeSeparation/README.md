# Compute separation with Aspire

A local development tool for running the real Functions Host, WorkerProxy, and .NET isolated `SampleIsolatedApp` together.

The tool supplies no Host-side `host.json` and sets the Host's existing read-only app-content option. The compute Host always requests worker delivery with the `HostConfiguration=1` initialization capability. If the first admitted worker sends no envelope, it selects file-based loading, which uses local missing-file defaults in this tool.

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

`project`, `project-identity`, and `container` link the worker automatically. `project` and `container` assign the worker pod first: AppHost sends both calls through the fake platform in `project`, and calls the WorkerProxy and Host directly in `container`. The identity profile starts linking while assignment waits for the worker, avoiding a circular wait on startup token acquisition. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

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

## Try the sample

Open the dashboard URL printed at startup. Find `functions-host` in project mode or `runtime` in container mode, open its `http` endpoint, and append `/api/hello`.

Once the Host has started, GET or POST requests return:

```text
Hello from the BYOC .NET isolated worker. Worker ID: aspire-<worker-guid>
```

Each worker gets an independently generated GUID-based ID, matching the identity used for its Host link.
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
The platform must use the worker's `StartStream` identity for `PUT /admin/workers/{workerId}`.
Scope correlation to the app/deployment and proxy instance; session IDs are local to one process.
Per-request HTTP and per-invocation RPC platform logs are not emitted. Existing HTTP activity instrumentation is unchanged.

Fields are bounded and encoded without splitting valid UTF-16 surrogate pairs; unsafe identities are fingerprinted rather than truncated.
Proxy diagnostic text is not sanitized. Payloads, assignment configuration and worker-authored logs
are excluded.

## Fake platform

WorkerProxy uses `WEBSITE_POD_NAME`, falling back to `CONTAINER_NAME`, for pod identity.
AppHost supplies `WEBSITE_POD_NAME` locally from `ComputeSeparation:PodName`.

In project mode, the `fake-platform` resource is a local stand-in for the platform. It sends the assign and link requests to the WorkerProxy and Host, and receives Host state through a prototype AppServer endpoint.

Open the `fake-platform` console logs in the dashboard to watch the assign and link calls in order. The fake is not wired in container mode.

One fake platform keeps an array of known worker proxies: `worker-proxy-1`, and `worker-proxy-2` in the multiple-worker
profile. Aspire supplies each entry's management address, mapped HTTP authority, runtime gRPC endpoint, and worker ID.
The assignment sends that scheme-less HTTP authority, including default ports, and publishes it as the effective
runtime origin. No proxy origin is configured at startup. Assign and link requests select an entry by name; an unknown name returns
`404` instead of choosing a different worker. Each worker has a distinct worker ID, request ID, pod name, and proxy
endpoints, and both link to the same Host.

`httpProxyEndpoint` is optional for non-HTTP workers. HTTP-capable workers require it in the accepted assignment
before successful HTTP initialization; supply a scheme-less mapped DNS name or IPv4 address with an explicit nonzero port, such as
`worker-proxy-1:28080`. IPv6 literals, including scoped addresses, are not supported for this assignment field.
There is no startup fallback: `WorkerProxy:HttpProxyEndpoint` and `WORKERPROXY__HTTPPROXYENDPOINT` are no longer consumed.
An accepted assignment without the field leaves the effective origin unset, so HTTP capability initialization fails
rather than advertising the worker's local address. The effective worker destination must have a different origin
from the mapped proxy endpoint to prevent a forwarding loop.

Roll out assignment producers together with this WorkerProxy version: each platform must send the mapped authority
for HTTP workers before initialization completes. Older producers that rely on the startup setting are not compatible with this
HTTP initialization flow. Most local profiles assign before linking. In `project-identity`, linking opens the runtime
transport early for token acquisition; WorkerProxy holds a successful HTTP initialization response until assignment
is accepted, without blocking runtime token traffic. Cancellation or relay termination cancels that wait.
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
| Fake platform | `GET /simulate/host/state` | Reads the latest prototype Host state publication, or returns `204` before one arrives. |

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

AppHost generates a random key for each run and gives it to both the Host and the fake platform. The fake platform uses it to authenticate and encrypt its `POST /admin/instance/assign` request, as the Host expects. The Host answers `202`, leaves placeholder mode, and specializes on its next request. Specialization reloads configuration without waiting for a linked worker or restarting a ScriptHost. Linking the first worker starts the ScriptHost independently.

## Container behavior

The dashboard has two independent resources, each backed by its own Compose project:

| Resource | Owns |
| --- | --- |
| `runtime` | The Host container and the shared Docker network. |
| `worker-pod-1` | WorkerProxy and the sample worker, sharing a network namespace. |

Stopping `worker-pod-1` removes only that pod's containers, leaving the same Host and network running. Stopping `runtime` stops the Host but keeps the network until AppHost shutdown. A small adapter runs these Compose groups because Aspire's native containers do not support the Proxy/worker shared-network arrangement.

`worker-pod.compose.yaml` is reusable for additional pods: each needs its own Compose project, Proxy alias, pod name (`WORKER_POD_NAME`, passed to WorkerProxy as `WEBSITE_POD_NAME`), worker/request identities, and management port, while joining the runtime's network. These values are visible in the resource environments. Aspire does not currently create additional container pods automatically; use the multiple-worker project profile above for the manual late-link flow.

Stop AppHost to stop the local run; normal shutdown removes worker pods before the runtime and its network. If AppHost is forcibly terminated, remove its `functions-aspire-...` groups in Docker Desktop. Relinking restarted workers is not automated; start a new AppHost session for another end-to-end run.

This tool uses local-development transport and authentication settings. Do not expose its endpoints outside your development machine.

## Opt-in Token Service proxy

The Host and WorkerProxy can forward managed-identity HTTP requests over the existing Host-initiated FunctionRpc stream. The Token Service stays in the Host environment; no Token Service sidecar or new cross-pod identity port is needed.

See [Token Service proxy configuration](../../src/Functions.WorkerProxy/README.md) for the required Host, WorkerProxy, and customer environment variables, configuration examples, limits, and failure behavior. The bridge is disabled by default; the `project-identity` profile below supplies its development-only settings automatically.

Host linking can proceed independently of WorkerProxy assignment; the manual harness's assign-before-link sequence is not a platform requirement. Customer-only restarts do not imply a new per-pod credential or a WorkerProxy restart.

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

The `Functions.Host.Tests` identity suite remains a faster automated socket/gRPC check. The interactive profile adds actual outbound HTTP and separate processes; neither substitutes for real Token Service interoperability, all-language qualification, or container network-isolation certification. Other existing profiles are unchanged and do not provision identity settings.

Worker restart, registration, assignment, and persistent pod-transport behavior remain owned by the existing WorkerProxy lifecycle. The registry-owned setup from [#12100](https://github.com/Azure/azure-functions-host/pull/12100) is integrated, but it does not change relay transport lifetime. This checkout still couples worker-stream termination to transport termination; the identity scenario is a first-start flow, not proof of preserved transport across customer-only restarts.

Enable production deployments only with the approved Host/WorkerProxy transport, authorization/revocation integration, and platform-provided settings. Do not expose the development transport or silently restore a worker-side Token Service as a fallback.

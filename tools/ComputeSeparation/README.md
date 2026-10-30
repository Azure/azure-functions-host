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
| `project-placeholder-manual-multiple-workers` | One placeholder Host, two WorkerProxies, two sample workers, and one fake platform. Assign and link each worker by name to test adding capacity after Host startup. |
| `container` | Three Linux containers: a ReadyToRun Host, Native AOT WorkerProxy, and ReadyToRun sample worker. |

`project` and `container` link the worker automatically. In `project`, AppHost asks the fake platform to assign the pod and then link the worker, in that order. Every profile starts an Aspire dashboard. Container images are built from the current worktree; the first build can take several minutes. Nothing is published or deployed remotely.

The equivalent command-line launches are:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project
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

## System telemetry

WorkerProxy emits only its own platform records, separately from ordinary ASP.NET Core/YARP console output.
Worker `RpcLog` messages (System, User, and CustomMetric) are forwarded unchanged to the Host without
being interpreted, filtered, or emitted as proxy telemetry.
The proxy pipeline is `ILogger` -> OpenTelemetry `LogRecord` -> `WorkerProxyLogExporter` ->
`WorkerPodEventGenerator` -> the shared `BufferedConsoleWriter` -> stdout. A simple export processor
avoids adding a second queue in front of the buffered writer. Exporter flush/shutdown drains preceding
buffered records, and flush honors its timeout without stopping subsequent logging.
The exporter uses the record's captured timestamp, trace/span IDs, formatted message and approved
scopes, not ambient activity at export time. It does not serialize arbitrary attributes or resource data.
This is a local OpenTelemetry **log exporter**, not an OTLP exporter or a new tracing/metrics pipeline.
The default `MS_FUNCTIONS_WORKER_POD_LOGS` prefix retains the existing positional format.
Both formats sanitize and bound diagnostic strings. The positional format preserves its 21 columns,
quotes delimiter-containing fields, removes line breaks, and represents unsafe invocation IDs as
`sha256:<digest>` in the existing invocation column. It does not carry the full V2 correlation context.
Startup build/bind failures emit a proxy-owned `WorkerProxyStartupFailed` record through a temporary
OpenTelemetry pipeline that does not depend on application services, and flush before exit. Only the
exception type is reported, not invalid configuration values. If the format setting is unavailable
or invalid during startup, this failure record uses the collector-compatible positional format.
Configure these settings **on the WorkerProxy process**, not in the assigned app's `host.json`:

| Setting | Default | Effect |
| --- | --- | --- |
| `WorkerProxy:UseStructuredSystemLogs` | `false` | Emit `MS_FUNCTIONS_WORKER_POD_LOGS_V2` followed by one compact JSON object instead of the positional record. |
| `WorkerProxy:EnableInvocationSystemLogs` | `false` | Include Debug relay and HTTP diagnostics, including per-invocation observations. Use for bounded diagnostic runs. |

Environment variable equivalents are `WorkerProxy__UseStructuredSystemLogs` and
`WorkerProxy__EnableInvocationSystemLogs`. Restart the proxy after changing them.
**Do not enable the V2 format in a deployed environment until its collector/parser supports it.**
The two formats are alternatives, not dual emission; neither setting changes the original RPC messages forwarded to the Host.

The V2 contract uses `SchemaVersion=2` and includes:

| Fields | Meaning |
| --- | --- |
| `EventTimestamp`, `Severity`, `EventName`, `Category` | UTC observation time and event identity. Ingestion time is separate. |
| `EmitterComponent`, `EmitterInstanceId`, `Sequence` | Proxy process identity and locally assigned emission sequence. Sort by sequence within one emitter; neither sequence nor clocks establish cross-process causality. |
| `OriginComponent` | Always `WorkerProxy`; worker-authored logs are not part of this stream. |
| `WorkerId`, `LinkId`, `ProxySessionId`, `InvocationId` | Optional correlation fields; unknown values are omitted, not fabricated. |
| `Operation`, `Stage`, `Outcome`, `ReasonCode`, `DurationMs` | Optional operation context, observed outcome and locally measured duration. |
| `Attributes` | Typed, allowlisted HTTP/RPC status, peer side and pod-state observations. Not arbitrary logger state or request data. |
| `TraceId`, `SpanId` | Existing W3C activity context when present. For RPC this may describe the long-lived transport, not an invocation span. No trace exporter or new span is created. |
| `Message`, `ExceptionType`, `Truncated`, `InvalidLinkId` | Bounded diagnostic text, optional exception type and explicit validation/size flags. Raw exception messages/stacks are not separate V2 fields. |

The Host generates one `LinkId` per connection attempt and sends it as `x-ms-functions-link-id`
gRPC metadata. Matching repeated link requests reuse the existing connection/ID; a new attempt gets a new ID.
Only metadata on the runtime-facing listener supplies this ID. Worker-side metadata cannot replace it.
Host link-start/init/failure logs include WorkerId and LinkId as structured ILogger properties and in their
messages. The existing Host system-log wire format is unchanged: consumers of that format still need to
extract these IDs from the message until their Host ingestion mapping is extended.

Before a Host attaches, use `(EmitterInstanceId, ProxySessionId)` to follow a proxy session. Later
peer/StartStream events associate it with WorkerId and LinkId. The local session counter alone is not unique.
Join invocation observations using the existing Host InvocationId, also carried on HTTP requests as
`x-ms-invocation-id`. Scope joins to the trusted app/deployment metadata supplied by the collector.

Proxy lifecycle/assignment/readiness logs are collected at Information.
Detailed invocation observations require the diagnostic setting. `ProtocolMessageObserved` distinguishes
request/response receipt from successful forwarding using Operation and Stage; RpcStatus is only a
worker-reported result. `HttpForwardingCompleted` reports the HTTP leg, **not final function completion**:
HTTP 200 and an RPC Failure can legitimately occur for the same InvocationId. Only the Host owns final
execution completion and capacity release; the proxy does not invent a capacity counter.

V2 strings are sanitized using the shared sanitizer, then bounded (Message: 1,024 UTF-16 code units;
Category/ExceptionType: 256; other diagnostic strings: 128). The fixed shape bounds serialized records
below 32 KiB including JSON escaping. Truncated text is flagged. Identifiers of at most 128 ASCII
letters/digits, `-`, `_`, or `.` retain their exact value; other opaque WorkerId/InvocationId values are
represented by separate lowercase SHA-256 `WorkerIdHash`/`InvocationIdHash` fields over the original UTF-8
bytes, never by silently truncating an identity. LinkId must be an `N`-format GUID; invalid values are
omitted and flagged without changing stream admission.

New operation records exclude bodies, function payloads/results, customer exceptions, assignment
environment and host.json. Worker RPC logs never enter the proxy telemetry pipeline.
Production rollout still requires collector mappings,
deployment enrichment, retention/rate policy and real ingestion validation.

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

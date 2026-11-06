# WorkerProxy system logs

WorkerProxy sends its own diagnostics through the existing OpenTelemetry **logging** provider to
`MS_FUNCTIONS_WORKER_POD_LOGS` on stdout. There are no new spans, exporters, telemetry resources,
propagation headers, or changes to `ActivityHeadersPropagator = null`. Worker-authored `RpcLog`
messages and RPC payloads are forwarded, not exported as proxy logs.

## Wire and identity contract

The 21 positional fields, quote/newline escaping, Details semantics, and 1,024-character Summary/Details
limits are unchanged. EventLevel remains **4 Information, 3 Warning, 2 Error, 1 Critical**.
The source timestamp is the captured log-record timestamp, not export/ingestion time.
The Host formatter names position 3 (one-based) AppName, but uses a unique-slot identity;
this repository does not establish that collector mapping or equivalent proxy authority.
Consequently this change leaves **all previously empty identity columns empty**.
WorkerId is not HostInstanceId; FunctionId is not FunctionName. Subscription, slot, runtime site,
Host version, and Host instance identity are not inferred.

Summary retains optional `WorkerId=...; ProxySessionId=...; ` prefixes, followed by a versioned
`WP1[key=value;...]; ` envelope and readable diagnostic text. Legacy records without WP1 remain valid.
Keys have a fixed serialization order. Values are invariant-culture tokens, never JSON in Details.
Absent values are omitted; zero/false are retained. The complete prefix is bounded to 896 characters,
reserving at least 128 characters of Summary. When a value would exceed this budget it is omitted
and `omit=1;` is appended **inside** the complete envelope; no partial tokens are emitted.
WorkerId retains its original ASCII identifier policy. Metadata permits ASCII letters, digits,
`-_.:+` up to 128 characters; other/oversized identities are `sha256:<lowercase-hex>`, not truncated
into collisions. Query the same fingerprint for such an identity. Build metadata includes the
assembly informational version (including its commit suffix), not collector SourceVersion or image identity.

| Key | Query field | Meaning |
| --- | --- | --- |
| p, a, g | ProxyPodName, ProxyAppName, FunctionGroupName | Platform pod and accepted assignment identity |
| r, sr | StateRevision, IdentitySnapshotRevision | Captured revision; terminal transition revision and pre-transition identity revision respectively |
| s, wr, rm | AssignmentState, WorkerReady, RemovalRequested | State in the identity snapshot, not an assertion of HTTP/Host readiness |
| q, i, f | RequestId, InvocationId, FunctionId | Server HTTP request ID or observed protocol IDs |
| op, ph, o, rc, sd | Operation, Phase, Outcome, ReasonCode, Side | Allowlisted operation classifications |
| sc, rpc, ye | StatusCode, RpcStatus, ForwarderError | Actual observed status and YARP result (not exception text) |
| ms, cs, at, rs | ElapsedMs, CancellationSource, Attempts, ResponseStarted | Operation timing, cancellation observation, probe attempts, partial response state |
| n, uf, pm | FunctionCount, UnsuccessfulCount, PendingMessages | Batch counts; queued messages only when the channel supports counting |
| b, omit | ProxyBuildVersion, MetadataOmitted | Build identity and explicit metadata-budget omission |

Context is an immutable operation/session snapshot. Assignment logs use the state captured atomically
with the assignment decision; delayed HTTP completion cannot replace it with a new session.
Rejected request identity, assignment Environment, FunctionAppDirectory, headers, URLs/queries, and
payloads are never added to context. HTTP does not trust a caller-supplied invocation ID header.
Before assignment/validated StartStream, missing app/worker IDs are expected.
RPC terminal/release/late-secondary records retain their session's accepted identity even when Runtime
loss clears live identity. `r` is the terminal transition revision and `sr` the retained identity snapshot
revision in those records; do not interpret retained identity as current pod admission.
HTTP and assignment revisions describe their operation's captured state, not a resampling at export.
If startup fails before options/state exist, only build identity and a safe failure category are available.

## Events and interpretation

Application, assignment, readiness, HTTP forwarding, and relay categories are explicitly exported at
Information and above; raw framework/YARP categories and broad Debug logging are not enabled.

| Events | Interpretation |
| --- | --- |
| WorkerProxyStarted / WorkerProxyStartupFailed | Safe build identity; pod when state exists. Failure text excludes invalid option values. |
| WorkerAssignmentRequestCompleted / Rejected / Failed / Canceled | One final request outcome, status/duration and captured accepted state. API decisions include Created, AlreadyAssigned, AssignmentConflict, WorkerTerminated, WorkerNotReady. A single validation error retains its API code (Required or InvalidValue); ValidationFailed aggregates multiple errors. Binding/auth failures use InvalidRequest, UnsupportedMediaType, Unauthenticated or Forbidden. |
| DestinationReady / DestinationNotReady / DestinationResolutionFailed | One result per uncached probe, with attempt count and duration, never one record per retry or a destination URL. DNS failure has zero TCP attempts. |
| WorkerHttpOutcome | One final admission/forwarding failure or caller cancellation, not successful-request telemetry. Readiness failures use the existing probe event instead. WorkerServerError is a completed worker 5xx, TransportFailure is a YARP/transport failure, Canceled is a cancellation observation. |
| PeerAttached / StartStreamValidated / PeerReleased / AttachmentRejected | Session and side with attach/validation/release phase or safe rejection reason. |
| ProtocolMessageObserved / FunctionLoadBatchRequested / FunctionLoadBatchCompleted | Received/forwarded init/load chronology, protocol status and available request/function IDs. Batches remain count-only. Missing/unknown/unsuccessful response results are warnings. |
| InvocationResponseFailed | One received unsuccessful/missing-result invocation response, with available invocation/request IDs. No receive/forward/success invocation records or per-invocation tracker. |
| SessionFaulted / SessionTerminated | First terminal cause, side, read/process/write/shutdown phase and session duration. A failed write includes IDs of the message being written, when available. Secondary failures cannot replace this cause. |
| WorkerProxyShutdownStarted / WorkerProxyStopped | Observed application shutdown and elapsed shutdown time, not an inferred OS exit reason. |

CancellationSource `Caller` means HTTP RequestAborted was observed; `PeerCall` means a gRPC call token
was canceled; `Shutdown` means explicit proxy shutdown was observed; `Unknown` makes no claim about
intent. Information severity or OperationCanceledException alone **does not mean healthy shutdown**.
A canceled gRPC transport can reflect a peer crash or network loss. A failed response write does
not prove user code never ran. Observed HTTP status may remain 2xx after a partial-response fault,
or the default 200 on cancellation before headers; use ResponseStarted and Outcome together.
There is no reliable outstanding-invocation count, no attribution for failures before the proxy,
and no evidence here that Host trigger listeners started.

The 8,000-record queue uses an opt-in nonblocking write. Full/closed buffers drop records rather than
waiting or synchronously falling back to stdout. Loss or writer failure schedules at most **one**
non-recursive stderr health notice per process; no logging retries are added. If stderr is also
unavailable the notice cannot be delivered. Host callers of the shared writer retain their old
behavior. Disposal remains a best-effort drain up to five seconds; abrupt death or a stalled sink can
lose records. Explicit ForceFlush remains unsupported. This is not a delivery guarantee.

## Query-local normalization and runbook

Paste [NormalizeWorkerProxyLogs.kql](NormalizeWorkerProxyLogs.kql) before a query. It defines a local
tabular helper, not a physical table migration or a saved-function deployment.
It preserves source columns and adds the typed fields above, HumanSummary, MetadataParseStatus,
MissingMetadataFields and MissingAssignedIdentity. Parse states distinguish Legacy, Parsed,
FieldsOmitted, UnsupportedVersion, MalformedEnvelope, DuplicateKey, InvalidTypedValue and
InvalidLegacySession. Do not silently treat parse errors as zeros or empty successful operations.
Missing identity is explicit and is not automatically an error before assignment/handshake.

Replace the cluster/database placeholders and confirm the collector's actual timestamp and
pod/container/image column names before running examples. Use a short time range and a qualified
destination; these examples do not establish live schema or ingestion compatibility.

```kusto
// Paste the helper above this query. Use source Timestamp, not ingestion time, for ordering.
let Logs = materialize(
    cluster("<cluster>").database("<database>").FunctionsWorkerPodLogs
    | where Timestamp between (datetime(2026-01-01) .. datetime(2026-01-01 01:00))
    | invoke NormalizeWorkerProxyLogs());
// App/group timeline, including earlier unassigned records on the same pod in this bounded window.
// Pod names/session IDs can recur: retain collector container/image identity and narrow further if needed.
let Pods = Logs
    | where ProxyAppName == "example-app" and FunctionGroupName == "http"
    | where isnotempty(ProxyPodName)
    | distinct ProxyPodName;
Logs
| join kind=leftsemi Pods on ProxyPodName
| project Timestamp, ProxyPodName, ProxyAppName, FunctionGroupName, ProxyWorkerId, ProxySessionId,
    StateRevision, IdentitySnapshotRevision, EventName, Operation, Phase, Outcome, ReasonCode, HumanSummary
| order by Timestamp asc
```

For each query below, prepend the helper and the time-filtered `Logs` definition above:

```kusto
Logs
| where Operation == "Assignment" and Outcome in ("Rejected", "Failed", "Canceled")
| summarize Requests=count() by ReasonCode, StatusCode, Outcome, ProxyAppName, FunctionGroupName
```

```kusto
Logs
| where Operation == "Http"
| summarize Requests=count(), p95Ms=percentile(ElapsedMs, 95) by Phase, Outcome, ReasonCode,
    CancellationSource, StatusCode, ForwarderError, ResponseStarted
```

```kusto
Logs
| where ProxyPodName == "example-pod" and ProxySessionId == 1
| where Operation in ("RpcSession", "Initialization", "FunctionLoad", "Invocation")
| project Timestamp, ProxyWorkerId, EventName, Side, Phase, ReasonCode, RpcStatus,
    CancellationSource, RequestId, InvocationId, FunctionId, HumanSummary
| order by Timestamp asc
```

```kusto
Logs
| where Operation == "Lifecycle"
| summarize FirstSeen=min(Timestamp), LastSeen=max(Timestamp),
    Starts=countif(EventName == "WorkerProxyStarted"),
    Stops=countif(EventName == "WorkerProxyStopped"), Builds=make_set(ProxyBuildVersion)
    by ProxyPodName
// A start without a stop in this window is not proof of a crash: inspect platform exit evidence.
```

```kusto
Logs
| where MetadataParseStatus !in ("Parsed", "Legacy") or MissingAssignedIdentity
| project Timestamp, EventName, MetadataParseStatus, MissingMetadataFields, Summary
| take 100
```

Ingestion lag, **only** after verifying a source Timestamp and enabled ingestion-time policy:

```kusto
cluster("<cluster>").database("<database>").FunctionsWorkerPodLogs
| where Timestamp > ago(1h)
| extend IngestedAt=ingestion_time()
| summarize MissingIngestionTime=countif(isnull(IngestedAt)),
    p50Lag=percentile(IngestedAt - Timestamp, 50), p95Lag=percentile(IngestedAt - Timestamp, 95)
```

Synthetic query checks (append after the helper; each StatusMatches value should be true):

```kusto
datatable(Summary:string, Expected:string)
[
    "WorkerId=worker; ProxySessionId=1; Legacy text.", "Legacy",
    "WP1[p=pod;r=0;wr=false;ms=0.000;]; Ready.", "Parsed",
    "WP1[p=pod;omit=1;]; Bounded.", "FieldsOmitted",
    "WP2[p=pod;]; Future.", "UnsupportedVersion",
    "WP1[p=pod;", "MalformedEnvelope",
    "WP1[p=pod;p=other;]; Duplicate.", "DuplicateKey",
    "WP1[r=not-a-number;]; Invalid.", "InvalidTypedValue",
    "WorkerId=worker; ProxySessionId=invalid; Legacy.", "InvalidLegacySession",
    "WP1[p=pod;s=Ready;]; Missing assignment identity.", "Parsed"
]
| invoke NormalizeWorkerProxyLogs()
| extend StatusMatches=MetadataParseStatus == Expected
| project Expected, MetadataParseStatus, StatusMatches, StateRevision, WorkerReady, ElapsedMs,
    MissingAssignedIdentity, MissingMetadataFields
```

## Qualification boundary

Local tests cover producer formatting, scopes, failure classification, races and existing forwarding
behavior. They do **not** validate the deployed image, actual collector parser/enrichment, Kusto
execution, table mapping, source-to-ingestion lag, or production performance.
Collector-owner review must verify all 21 positions and escaping against the real parser and
establish deployed source/image provenance before any separately authorized live qualification.
Do not install these queries as database functions or change a collector/table automatically.

Before rollout, agree on measured throughput, p95/p99 forwarding latency and allocation budgets
against the baseline on the same runtime/image. Structural bounds here are 1,024 Summary characters,
8,000 queued records, one loss notice per process, zero successful-HTTP/invocation records, one final
HTTP diagnostic per failed forwarding operation, and one received failure record per unsuccessful
invocation response. Failure traffic itself can still be high-volume. No diagnostic tracking grows
with the number of invocations. Numeric latency/allocation and acceptable loss-rate budgets remain
an external qualification decision, not a result inferred from unit tests.

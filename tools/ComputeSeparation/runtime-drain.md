# Runtime drain

The Runtime drain endpoint stops the current Host process from accepting new Worker links, drains the Workers linked to that Runtime, and closes those Runtime-facing links.

## Management API

Send an authenticated request to:

```http
POST /admin/runtime/drain
Content-Type: application/json

{
  "drainMode": "Phased"
}
```

`drainMode` is case-sensitive and accepts:

- `Phased`
- `Immediate`

The request body must contain only `drainMode`. Invalid, duplicate, or differently cased properties are rejected through the existing management API error envelope.

The request binds to a nullable drain-mode enum through a type-scoped System.Text.Json formatter. Other Host endpoints retain their existing formatters. Strict parsing preserves case-sensitive mode names and the existing distinction between malformed bodies, missing values, and invalid values; numeric values, numeric strings, and combined enum names are not accepted.

The first accepted request returns `202 Accepted` with the selected mode and acceptance time. Equivalent replays return the original acceptance time. A phased drain can be escalated to immediate, but an immediate drain cannot be downgraded.

If worker selection or phased scheduling fails unexpectedly, the operation retains the original failure. Subsequent requests surface that failure through the Host's existing error handling instead of returning `202`, including retries after a failed initial selection or escalation. They do not restart selection, reopen admission, change the frozen worker set, or reset already selected workers' deadlines. This does not apply to ordinary invocation failures, which remain part of the shared worker drain lifecycle.

## Process-local behavior

The first accepted request permanently closes Worker-Link admission for the current Host process. Registered links form the frozen Runtime drain set, including links still preparing an extension payload. These pending links remain in the snapshot for drain but cannot become available after admission closes. Repeated admission-close calls return the same read-only snapshot, even after Workers unlink; a disposed registry still rejects the call. A link still completing its handshake must recheck the admission fence and fail instead of joining after the snapshot.

Immediate drain selects every Worker in the frozen set at once.

Phased drain selects:

- non-HTTP Workers at 0, 10, 20, 30, and 40 seconds;
- HTTP Workers at 35 seconds.

Escalation to immediate selects every remaining Worker without changing the first-selection time of Workers already selected.

Root Host shutdown cancels the remaining phased selections. Disposing the coordinator cancels asynchronously without holding its operation lock while cancellation callbacks complete, then waits for its schedule to finish. Expected shutdown cancellation or registry-disposal races do not produce schedule-failure errors; unexpected scheduling failures still do.

## Shared worker lifecycle

Runtime drain reuses the existing worker retirement path:

1. `RpcClientWorkerChannel` atomically stops accepting new invocations.
2. The channel sends `WorkerDraining` to WorkerProxy and allows work accepted before that fence to reach a terminal result.
3. `WorkerChannelRegistry` uses its shared drain and unlink path, including `WorkerTerminate` and its grace period when supported, to close and dispose the Runtime-facing connection.

HTTP workers retain their startup group and validated concurrency capabilities. Selecting an HTTP worker for drain immediately withdraws its advertised Runtime capacity, even while accepted work is still completing on the linked channel. Non-HTTP scale-in continues to select only its exact function group and always-ready class.

Scale-in, idle retirement, and whole-Runtime drain share one invocation-drain task and one unlink owner per Worker. If another retirement selected a Worker first, Runtime drain adds only the Runtime deadline to that existing operation.

HTTP Always Ready scale-in commits its selected Workers to drain before the change-driven AppServer publisher sends the updated capacity. Whole-Runtime drain also includes HTTP on-demand Workers. If it arrives during a pending capacity publication, it still selects the frozen Runtime set; a later publication failure cannot reopen admission or restore draining Workers' capacity.

Accepted work is tracked by the shared `WorkerChannel` lifecycle. Drain waits for all accepted results, including when another invocation fails or is canceled. Deadline expiry uses the same shared shutdown path as channel disposal, preserving queued/executing invocation completion and failure telemetry without a separate Runtime drain invocation tracker.

Each Worker has a fixed 60-minute deadline measured from its first Runtime drain selection. If accepted work has not completed by then, the Runtime fails that work and continues through the same unlink path.

Closing the Runtime-facing connection lets the existing WorkerProxy state flow publish the Worker link as `Unlinked`. Downstream WorkerProxy removal remains owned by W2 and is not duplicated here.

## Intentional non-goals

Drain state is not persisted. A Host restart loses the accepted request, frozen snapshot, schedule, and admission fence. This change does not add drain generations, Host-incarnation correlation, an AppServer replay protocol, or downstream WorkerProxy removal.

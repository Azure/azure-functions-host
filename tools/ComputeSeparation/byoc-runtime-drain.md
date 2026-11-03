# BYOC Runtime drain

The BYOC Runtime drain endpoint stops the current Host process from accepting new Worker links, drains the Workers linked to that Runtime, and closes those Runtime-facing links.

## Management API

Send an authenticated request to:

```http
POST /admin/functions/byocdrain
Content-Type: application/json

{
  "drainMode": "Phased"
}
```

`drainMode` is case-sensitive and accepts:

- `Phased`
- `Immediate`

The request body must contain only `drainMode`. Invalid, duplicate, or differently cased properties are rejected through the existing management API error envelope.

The first accepted request returns `202 Accepted` with the selected mode and acceptance time. Equivalent replays return the original acceptance time. A phased drain can be escalated to immediate, but an immediate drain cannot be downgraded.

## Process-local behavior

The first accepted request permanently closes Worker-Link admission for the current Host process. Links already initialized at that point form the frozen Runtime drain set. A link that is still initializing must recheck the admission fence and fail instead of joining after the snapshot.

Immediate drain selects every Worker in the frozen set at once.

Phased drain selects:

- non-HTTP Workers at 0, 10, 20, 30, and 40 seconds;
- HTTP Workers at 35 seconds.

Escalation to immediate selects every remaining Worker without changing the first-selection time of Workers already selected.

## Shared worker lifecycle

Runtime drain reuses the existing P1 worker retirement path:

1. `RpcClientWorkerChannel` atomically stops accepting new invocations.
2. Work accepted before that fence is allowed to reach a terminal result.
3. `WorkerChannelRegistry` uses its existing unlink path to close and dispose the Runtime-facing connection.

HTTP workers retain their startup group and validated concurrency capabilities. Selecting an HTTP worker for drain immediately withdraws its advertised Runtime capacity, even while accepted work is still completing on the linked channel. P1 non-HTTP scale-in continues to select only its exact function group and always-ready class.

P1 scale-in and whole-Runtime drain share one invocation-drain task and one unlink owner per Worker. If P1 selected a Worker first, Runtime drain adds only the Runtime deadline to that existing operation.

Each Worker has a fixed 60-minute deadline measured from its first Runtime drain selection. If accepted work has not completed by then, the Runtime fails that work and continues through the same unlink path.

Closing the Runtime-facing connection lets the existing WorkerProxy state flow publish the Worker link as `Unlinked`. Downstream WorkerProxy removal remains owned by W2 and is not duplicated here.

## Intentional non-goals

Drain state is not persisted. A Host restart loses the accepted request, frozen snapshot, schedule, and admission fence. This change does not add drain generations, Host-incarnation correlation, an AppServer replay protocol, or downstream WorkerProxy removal.

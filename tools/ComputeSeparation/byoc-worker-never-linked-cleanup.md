# BYOC WorkerProxy never-linked cleanup

## Contract

RCP calls `PUT /admin/workers/{workerId}` on the Runtime with WorkerProxy's Runtime-facing
FunctionRpc endpoint. `WorkerChannelRegistry` opens that FunctionRpc connection before it waits for
worker initialization and publishes the initialized channel. WorkerProxy therefore uses acceptance
of the Runtime-facing FunctionRpc stream as the existing Runtime-connection milestone.

This deadline does not wait for the customer Worker connection, `StartStream`,
`WorkerInitResponse`, assignment, or full Link API completion.

## Behavior

- WorkerProxy starts one non-resetting deadline when its pod state is created, five minutes by default.
- `WorkerProxy:NeverLinkedTimeout` can override the duration before startup.
- The first accepted Runtime-facing FunctionRpc stream completes the deadline permanently.
- Expiry publishes sticky `RemovalRequested` through `POST /admin/infra/instanceState`.
- Removal closes new Runtime and Worker FunctionRpc admission, withdraws worker readiness, and blocks HTTP
  forwarding. Repeated state polls continue to return `RemovalRequested`.
- Removal clears the advertised HTTP proxy origin and sets HTTP capacity to zero. A first late
  assignment is rejected; an equivalent assignment replay leaves capacity at zero.
- Legion remains responsible for deleting the pod.

The deadline uses `TimeProvider`'s monotonic timestamp. Configuration reload, worker activity, and
assignment do not reset or complete it.

Removal does not cancel already attached streams or an HTTP request already being forwarded. Those
operations end when their existing transport ends or Legion deletes the pod.

## Rollout dependency

The Runtime-facing FunctionRpc request must be authenticated before this cleanup is enabled in a
stamp. Deploy the authenticated Runtime Link path first, then validate that Runtime connects to the
WorkerProxy endpoint supplied by RCP.

If the intended milestone changes from "Runtime connection established" to "Link API completed
after worker initialization and registry publication," WorkerProxy needs a new explicit
acknowledgment from Runtime; that signal does not exist in the current contract.

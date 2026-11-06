# BYOC worker scale-in

## Contract

Legion owns app-level demand. This API applies one absolute retained count to the currently usable Workers in
one exact non-HTTP function group and capacity class, or the HTTP Always Ready class:

```http
POST /admin/workers/scale-in
Content-Type: application/json
```

```json
{
  "functionGroup": "durable",
  "isAlwaysReady": false,
  "desiredWorkerCount": 1
}
```

Success returns the number of Workers newly committed to drain:

```json
{
  "newlyDrainingWorkerCount": 2
}
```

For HTTP Always Ready workers, set `"functionGroup": "http"` and `"isAlwaysReady": true`. HTTP group matching
is case-insensitive, consistent with assignment classification. Non-HTTP group matching remains exact.

The HTTP response reports workers committed to the shared drain lifecycle; it does not wait for capacity
publication, accepted work to finish, or physical Worker deletion. The existing change-driven AppServer publisher
observes the resulting Runtime state. Repeating the same retained count is a no-op after enough matching Workers
have entered drain.

Requests fail with `400` when the body is malformed, a required field is missing, the desired count is
negative, or the request targets on-demand HTTP capacity (`"isAlwaysReady": false`).
The existing Host admin authorization policy protects the route.

## Selection

Host considers only channels that:

- completed Worker initialization and current ScriptHost setup;
- remain ready for invocations;
- exactly match the WorkerProxy-supplied function group and always-ready class; and
- have not already entered drain.

The request carries no Worker identity, age, load, or priority signal. Workers within the matching population
are therefore interchangeable for this contract, and the API does not guarantee a selection order.

Starting, setup-pending, terminal, and already-draining Workers are excluded. This is a one-shot decision over
the current usable population; a later Legion evaluation must correct capacity that changes after the request.

## Drain lifecycle

WorkerProxy replaces worker-supplied classification capabilities with its accepted assignment before Host
sees them. Host then uses the existing registry and channel lifecycle:

1. The registry calculates the excess matching count under its state lock.
2. The selected channel closes invocation admission under the existing lifecycle lock shared by dispatch and
   idle retirement. The registry signals `RuntimeStateManager`; the change-driven publisher sends the resulting
   state through the existing AppServer `PUT /admin/infra/host/state` client. Dispatch using an older ready
   snapshot rechecks the lifecycle lock and tries another channel in the invocation's function group. Existing
   invocation attribution and on-demand HTTP activity tracking are preserved. The channel also sends the
   `WorkerDraining` control message to WorkerProxy; it is consumed there, not forwarded to the language worker,
   and updates the current pod state to Legion's `Draining` status.
3. The same drain-and-unlink path handles count-based scale-in and on-demand HTTP idle retirement; their entry
   points differ only in worker selection. Idle retirement selects only on-demand HTTP workers with zero active
   requests, while this API selects excess workers by function group and capacity class.
4. The selected channel drains the base `WorkerChannel` accepted-invocation tracking used by `TryPostInvocation`
   and shutdown, including queued inputs and input conversion. The drain waits for all accepted results even if
   an earlier result fails or is canceled. Channel disposal uses that same shutdown completion path for work that
   cannot reach the worker, so neither the invocation nor drain can hang in the input buffer.
5. For HTTP proxy invocations, the result remains pending until `DefaultHttpProxyService` finishes forwarding
   the response, even if the worker has already sent its successful gRPC response. This keeps the Runtime link
   open while an accepted HTTP response is still streaming.
6. After those invocations reach a terminal result, the registry verifies the selected channel still owns its
   slot and detaches it. If the worker advertised `HandlesWorkerTerminateMessage`, Host sends `WorkerTerminate`
   with the existing five-second grace period and waits for WorkerProxy to close the Host-side connection as the
   worker exits. If the connection does not close in time, or sending the message fails, Host disposes the channel
   to force the disconnect. Workers without that capability are disconnected directly. The empty slot is then
   removed; concurrent explicit unlink or terminal-channel cleanup is a no-op for the deferred drain cleanup.
   Successfully admitted worker IDs remain terminal after unlink and cannot be reused in the same registry.

Invocation success, failure, and cancellation all satisfy drain completion. The API adds no invocation-drain
timeout: forcibly closing a channel before accepted work reaches a terminal result would violate the drain
contract. The separate WorkerTerminate handshake is bounded by the existing five-second grace period.

Host scale-in diagnostics use event `710` for committed selections and `712` for request cancellation before
commitment. The existing AppServer publisher records successful outbound snapshots as event `1000` and reports
rejected or failed publication attempts separately.

## Scope and dependencies

This PR does not:

- add on-demand HTTP scale-in;
- retain a standing target or cancel future Worker creation;
- redistribute Workers across Runtimes;
- publish WorkerProxy `RemovalRequested` state; or
- physically delete Workers.

Production physical scale-in still depends on the separate removal-publication flow that translates Runtime
link closure into a Legion-visible removal request.

## Behavioral coverage

The hosted public API tests cover:

- absolute retained count and repeated-request behavior;
- exact function-group and capacity-class isolation;
- group-aware routing and invocation attribution from a stale ready snapshot after scale-in;
- explicit unlink during drain preserving terminal worker IDs and workers outside the selected class;
- mixed HTTP Always Ready/on-demand workers, zero/nonzero retention, and repeated/concurrent requests;
- successful outbound AppServer publication before admission closes or the link disconnects;
- rejection/cancellation without drain, reservation rollback, and a later successful request;
- a real HTTP response finishing after its gRPC result and before selected-worker removal;
- immediate intake withdrawal followed by accepted-work completion or terminal channel failure and link close; and
- rejection of HTTP scale-in.

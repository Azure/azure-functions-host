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

The HTTP response waits for successful reduced-capacity publication to AppServer before committing drain, but
does not wait for accepted work to finish or physical Worker deletion. Repeating the same retained count is
a no-op after enough matching Workers have already entered drain. HTTP selections are serialized, including
concurrent repeated requests, so two requests cannot reserve the same worker or drain below their retained target.

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
2. For HTTP Always Ready workers, the registry reserves selected workers' advertised capacity while keeping
   invocation admission open. It wakes `RuntimeStateManager`; selected slots no longer contribute HTTP capacity,
   but their links still contribute to the linked worker count.
3. The Host publisher refreshes runtime state and sends the reduced capacity through the existing AppServer
   `PUT /admin/infra/host/state` client. Publication is serialized with background and final-shutdown sends,
   so a stale in-flight publication cannot overtake the reduction. Drain begins only after a successful HTTP
   response from AppServer; that response is not an applied-state acknowledgement.
4. The selected channel closes invocation admission under the same gate used by dispatch. Dispatch using an
   older ready snapshot rechecks that gate and tries another channel. Non-HTTP scale-in skips the publication
   barrier because it does not reduce HTTP capacity.
5. The selected channel drains the base `WorkerChannel` accepted-invocation tracking used by `TryPostInvocation`
   and shutdown, including queued inputs and input conversion. The drain waits for all accepted results even if
   an earlier result fails or is canceled. Channel disposal uses that same shutdown completion path for work that
   cannot reach the worker, so neither the invocation nor drain can hang in the input buffer.
6. For HTTP proxy invocations, the result remains pending until `DefaultHttpProxyService` finishes forwarding
   the response, even if the worker has already sent its successful gRPC response. This keeps the Runtime link
   open while an accepted HTTP response is still streaming.
7. After those invocations reach a terminal result, the registry uses the existing unlink path to close and
   dispose the channel.

Invocation success, failure, and cancellation all satisfy drain completion. The API adds no drain timeout:
forcibly closing a channel on a timer would violate the accepted-work drain contract. Registry shutdown still
cancels the background wait and owns final channel disposal.

### Publication failure and cancellation

The publication barrier uses the existing AppServer publisher's transient-error retry/backoff policy and existing
HTTP request timeout; it adds no new timeout, retry policy, or configuration. While publication is pending,
selected workers remain invocation-ready. The lower advertised capacity informs AppServer that these slots
are about to leave before Host closes their admission gates.

An authentication rejection fails the scale-in request with `503` and `CapacityPublicationFailed`. Cancellation
aborts publication/retries. Neither outcome commits selected workers to drain: their capacity reservations are
released, runtime state is signaled to restore their advertised capacity, and a later request can select them.
Unexpected publication or tracking errors propagate instead of silently draining. A partially delivered or
successfully published reduction followed by cancellation can temporarily under-advertise capacity until the
restored state is published; it cannot retire a worker without the successful publication barrier.

Host scale-in diagnostics use event `710` for committed selections, `711` for a failed required capacity
publication, and `712` for request cancellation before commitment. The existing AppServer publisher records
successful outbound snapshots as event `1000` and reports rejected or failed publication attempts separately.

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
- mixed HTTP Always Ready/on-demand workers, zero/nonzero retention, and repeated/concurrent requests;
- successful outbound AppServer publication before admission closes or the link disconnects;
- rejection/cancellation without drain, reservation rollback, and a later successful request;
- a real HTTP response finishing after its gRPC result and before selected-worker removal;
- immediate intake withdrawal followed by accepted-work completion or terminal channel failure and link close; and
- rejection of HTTP scale-in.

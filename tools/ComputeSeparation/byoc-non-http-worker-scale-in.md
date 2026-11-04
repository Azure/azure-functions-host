# BYOC non-HTTP worker scale-in

## Contract

Legion owns app-level demand. This API applies one absolute retained count to the currently usable Workers in
one exact non-HTTP function group and capacity class:

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

The response does not wait for drain completion or physical Worker deletion. Repeating the same retained
count is a no-op after enough matching Workers have already entered drain.

Requests fail with `400` when the body is malformed, a required field is missing, the desired count is
negative, or the function group is HTTP. The existing Host admin authorization policy protects the route.

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
2. The selected channel closes invocation admission under the same gate used by dispatch.
3. Dispatch using an older ready snapshot rechecks that gate and tries another channel.
4. The selected channel drains the base `WorkerChannel` accepted-invocation tracking used by `TryPostInvocation`
   and shutdown, including queued inputs and input conversion. The drain waits for all accepted results even if
   an earlier result fails or is canceled. Channel disposal uses that same shutdown completion path for work that
   cannot reach the worker, so neither the invocation nor drain can hang in the input buffer.
5. After those invocations reach a terminal result, the registry uses the existing unlink path to close and
   dispose the channel.

Invocation success, failure, and cancellation all satisfy drain completion. The API adds no drain timeout:
forcibly closing a channel on a timer would violate the accepted-work drain contract. Registry shutdown still
cancels the background wait and owns final channel disposal.

## Scope and dependencies

This PR does not:

- add HTTP scale-in;
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
- immediate intake withdrawal followed by accepted-work completion or terminal channel failure and link close; and
- rejection of HTTP scale-in.

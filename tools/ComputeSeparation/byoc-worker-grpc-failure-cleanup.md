# BYOC W3 customer Worker gRPC loss cleanup

## Stack relationship

This W3 change is stacked on the P2/P3 Runtime-link-loss behavior. That lower layer owns unexpected Runtime EventStream loss; this change adds the equivalent ownership rule only for the established customer Worker EventStream.

## Contract

A customer Worker EventStream owns W3 only after its existing first-message callback accepts a nonempty `StartStream(workerId)`. If that same Worker-facing stream later ends through EOF, cancellation, read failure, or write failure, WorkerProxy requests sticky pod removal.

Pre-`StartStream`, rejected, stale, Runtime-facing, and planned application-shutdown handlers do not request W3. The existing session ID and Worker ID are the ownership record; this change adds no second establishment state or monitor.

## Flow

```mermaid
flowchart TD
    A[Worker EventStream] --> B{Valid StartStream accepted?}
    B -->|No| C[Preserve replacement behavior]
    B -->|Yes| D{Worker stream unexpectedly ended?}
    D -->|No| E[Keep existing behavior]
    D -->|Yes| F{Application stopping?}
    F -->|Yes| G[Normal shutdown]
    F -->|No| H[Set sticky RemovalRequested]
    H --> I[Reject new streams, assignments, and HTTP work]
    I --> J[Publish RemovalRequested until Legion deletes the pod]
```

## Observable behavior

| Scenario | Result |
|---|---|
| No Worker or pre-`StartStream` failure | The handler can be replaced. |
| Established Worker EOF, cancellation, read failure, or write failure | `RemovalRequested` becomes sticky. |
| Runtime-facing stream failure | The W2 base continues to request removal; this PR does not change it. |
| Planned WorkerProxy shutdown | No removal request is published. |

After removal, Worker readiness is withdrawn, HTTP forwarding returns 503, and new Runtime and Worker EventStreams return the existing gRPC `Unavailable` response. An equivalent retry of an existing assignment returns `WorkerTerminated`; a first assignment after an unassigned established Worker is lost returns `WorkerNotReady`. The management endpoint stays available and repeatedly publishes the same unhealthy `RemovalRequested` state.

## Implementation

- `FunctionRpcRelay.Session` reuses first-message and terminal callbacks to classify Worker-side stream failure.
- `FunctionRpcRelay` observes the host's `ApplicationStopping` token to close stream admission and suppress loss-driven removal before its hosted shutdown methods run. Explicit relay shutdown remains covered by the existing shutdown markers.
- `WorkerPodStateManager` atomically verifies the current session has a Worker ID, excludes shutdown, and commits sticky removal.
- `FunctionRpcRelay`, assignment, and HTTP forwarding reuse the central state gate to close later admissions.
- `WorkerOriginState` and the existing long-poll endpoint publish the deletion request.

## Behavioral coverage

Hosted tests cover pre-establishment replacement, established EOF with all admission gates, established cancellation, and application shutdown. The shutdown regression delays a preceding hosted lifecycle service after `ApplicationStopping` while either stream reaches EOF or is canceled, proving that admission is closed and no sticky removal is requested before relay shutdown runs. The pre-establishment cancellation case waits for Worker admission without sending `StartStream`, so cancellation cannot leave an unrelated Runtime-only session waiting. The existing relay termination theory adds deterministic Worker read- and write-failure coverage.

The established EOF theory covers the mapped HTTP proxy origin and default and explicit HTTP concurrency in both startup modes, including Worker loss after a Host drain notification. W3 retains the terminated assignment, requested configuration/content versions, and capacity for the terminal assignment contract without admitting new work or exposing the terminated session's origin; Runtime-side removal continues to clear live assignment identity and capacity while preserving function-group and version attribution. Both paths preserve the pod startup time. Terminal logs retain the accepted Worker ID and session correlation, and rejected replacement streams still emit rejection diagnostics.

Worker completion while HTTP initialization waits for assignment requests sticky W3 removal and releases the pending waiter, with or without messages queued behind initialization. An accepted `StartStream` already establishes the customer Worker even when assignment is pending; only pre-establishment failures allow replacement.

The tests are in-memory hosted WorkerProxy validation. They do not prove deployed Legion polling or physical pod deletion.

## Non-goals

W1 Runtime Link timing, changes to W2 Runtime-side removal, W4 drain behavior, restart protocols, `AcknowledgeLink`, authentication, TLS, Docker, and Legion implementation changes remain outside this PR.

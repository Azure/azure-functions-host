# Runtime and Worker disconnect cleanup

WorkerProxy requests removal when an admitted Runtime-facing FunctionRpc stream ends unexpectedly
through EOF, cancellation, read failure, or write failure, even before Worker initialization.
The same removal path applies when an admitted customer Worker-facing stream ends unexpectedly,
both before and after assignment. Worker admission requires a validated first `StartStream` with
a nonempty worker ID accepted by the current pod state.
The existing relay completion path publishes one sticky removal decision; no extra monitor,
startup deadline, or lifecycle state machine is added.

Duplicate and rejected streams, Worker failures before accepted `StartStream`, and stale handlers
do not select removal. Application-owned shutdown is excluded from disconnect cleanup.

Removal clears live worker and assignment identity and publishes `RemovalRequested` through
`POST /admin/infra/instanceState`. An empty body returns the current checkpoint; posting that
checkpoint waits up to 60 seconds for changes (HTTP 204 if unchanged), including after removal.
A new removal revision wakes existing polls immediately. The existing worker state endpoint remains available.

The shared checkpoint contracts and Runtime identity/initialization wiring are aligned with
`manikantanallagatla/azure-functions-host` PR #20 at `2c4b21bd`. Only the contract prerequisites
are reused here; never-linked timeouts, scale-in, and drain are not included.
The infrastructure `self` identity retains the shared contract's placeholders. Runtime identity
comes from the Runtime-only `x-ms-functions-runtime-identity-bin` metadata. A correlated successful
WorkerInit response written to that Runtime publishes `Running` / `Linked` for a ready assignment;
stream admission alone does not. Assignment configuration/content timestamps populate the checkpoint.
Host forwards the optional serialized `runtimeIdentity` from `PUT /admin/workers/{workerId}` through
the registry to this binary metadata; exact link retries must retain the same identity.

Removal preserves a previously established `Linked` relationship and its Runtime identity without
preserving live readiness. A Worker that never completed initialization remains `Unlinked`.

Removal withdraws worker readiness, rejects new Runtime and Worker FunctionRpc streams, and
returns HTTP 503 for new forwarding requests, including those finishing endpoint-readiness checks.
The relay terminates the stream pair through its existing teardown path. Requests already being
forwarded are not canceled. `/admin/instance/ready` stays available for management health.
Legion remains responsible for physical pod deletion.

**Deployment prerequisite:** authenticate the Runtime-facing FunctionRpc endpoint before rollout.
Authentication and a rollout switch are outside this change.

# Token Service proxy configuration

The compute-separated `Functions.Host` and `Functions.WorkerProxy` can forward managed-identity HTTP requests over their existing Host-initiated gRPC stream. The Token Service runs only in the Host environment; no worker-pod Token Service or additional cross-pod Token Service listener is needed. The feature is **disabled by default** and must be enabled on both products. These settings supplement existing worker registration, listener, and Host-link configuration.

## Required environment

**Host process** (use the platform's existing app-scoped HTTP(S) Token Service endpoint and credential; no URL credentials or fragment):

```text
Functions__TokenServiceProxy__Enabled=true
MSI_ENDPOINT=http://127.0.0.1:8082/MSI/token
MSI_SECRET=<host-only-token-service-secret>
```

**WorkerProxy container**:

```text
WorkerProxy__TokenServiceProxy__Enabled=true
WorkerProxy__TokenServiceProxy__Port=8081
WorkerProxy__TokenServiceProxy__TokenPath=/msi/token
IDENTITY_HEADER=<random-per-pod-secret>
```

**Customer/language-worker container**, sharing WorkerProxy's pod network:

Modern clients:

```text
IDENTITY_ENDPOINT=http://127.0.0.1:8081/msi/token
IDENTITY_HEADER=<same-random-per-pod-secret>
```

Legacy clients:

```text
MSI_ENDPOINT=http://127.0.0.1:8081/msi/token
MSI_SECRET=<same-random-per-pod-secret>
```

Generate and inject the same per-pod secret into WorkerProxy as `IDENTITY_HEADER` and into the customer using the credential variable for its SDK contract. The customer's legacy `MSI_SECRET` is the local pod credential; it is **not** the Host's separate upstream `MSI_SECRET`. Do not set `IDENTITY_ENDPOINT` or customer credential variables on WorkerProxy. WorkerProxy binds only to loopback; reserve a port distinct from its management/gRPC listeners and do not publish it outside the pod. `Port` and `TokenPath` above show their defaults.

WorkerProxy accepts exactly one local credential header: modern `X-IDENTITY-HEADER` or legacy `Secret`. It validates either against the same injected per-pod secret, then removes it; Host adds its separate upstream credential. The local token path is matched case-insensitively and need not match `MSI_ENDPOINT`. Host always uses the configured upstream authority/path, retains any configured query, and appends the incoming raw query without decoding or interpreting it. Other local routes, including Token Service management routes, are not exposed. Neither component parses token bodies or caches tokens.

Routine Host credential rotation affects new sends; already-sent requests may finish. Explicit revocation must cancel affected work through the approved transport integration.

## Limits and timeouts

Use the keys below under `Functions:TokenServiceProxy` on Host and `WorkerProxy:TokenServiceProxy` on WorkerProxy. For environment variables, replace `:` with `__`, for example `WorkerProxy__TokenServiceProxy__RequestTimeout=00:00:45`. Message limits negotiate down to the smaller peer value; aggregate byte budgets stay local and are enforced independently.

| Key | Default | Scope |
| --- | --- | --- |
| `MaxRequestBodyBytes` | `1048576` (1 MiB) | Both |
| `MaxResponseBodyBytes` | `1048576` (1 MiB) | Both |
| `MaxHeaderBytes` | `32768` | Both |
| `MaxHeaderCount` | `100` | Both |
| `MaxTargetBytes` | `8192` | Both |
| `MaxPendingRequests` | `32` | Host per transport; WorkerProxy locally |
| `MaxBufferedBytes` | `67108864` (64 MiB) | Host per transport; WorkerProxy locally |
| `MaxHostRequests` | `256` | Host-wide across transports |
| `MaxHostBufferedBytes` | `268435456` (256 MiB) | Host-wide across transports |
| `RequestTimeout` | `00:00:30` | Total local request deadline; Host also caps the forwarded budget |
| `LinkTimeout` | `00:02:00` | Host registry-owned early-link attempt |

Equivalent Host JSON configuration, with an example smaller per-transport budget:

```json
{
  "Functions": {
    "TokenServiceProxy": {
      "Enabled": true,
      "MaxPendingRequests": 16,
      "MaxBufferedBytes": 33554432,
      "RequestTimeout": "00:00:30",
      "LinkTimeout": "00:02:00"
    }
  }
}
```

Body limits are **provisional, configurable engineering defaults**, not Token Service or SDK maxima. Admission reserves anticipated body/serialization copies and metadata, not just actual payload size. With default bounds, each request reserves `6430720` bytes, allowing at most 10 per transport and 41 Host-wide despite the higher count limits. These reservations are not a cap on total process/deserialization memory. Each byte budget must accommodate at least one full reservation.

## Startup and failure behavior

Identity calls can precede customer `StartStream`/`WorkerInit`. Waiting for a transport, Host preparation, HTTP forwarding, and local response delivery share one request deadline. Canceling an administrative link HTTP caller ends only that caller's wait; the registry retains its bounded link attempt. Proxy Hello/Ready is not worker initialization or upstream health.

Saturation returns `429` with `Retry-After: 1`, without a capacity wait queue. Local oversized bodies/headers return `413`/`431`; upstream transport/response-limit failures return `502`; unavailable transport or prepared-but-invalid Host configuration returns `503`; deadline expiry returns `504`. Upstream HTTP statuses and retry headers otherwise pass through. Cancellation retains capacity until terminal acknowledgment or transport cleanup; queued terminal responses retain Host reservations until physical write/discard.

Automatic upstream HTTP-client URI logging is disabled. With the proxy enabled, WorkerProxy suppresses request-URI logs below Warning in `Microsoft.AspNetCore.Hosting.Diagnostics`, including provider-specific overrides, on all its listeners. Stricter filters and feature-off logging are unchanged. Do not log credentials, customer queries, or token bodies elsewhere.

For a local mock-backed run:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-identity
```

See the [compute-separation harness guide](../../tools/ComputeSeparation/README.md#debug-the-complete-first-start-flow) for mock controls and debugging. Production requires the approved authenticated Host/WorkerProxy transport, platform settings, and authorization/revocation integration. Real Token Service interoperability, all-language qualification, and persistent transport across customer-only restarts remain separate qualification/lifecycle work; this feature does not redesign registration, assignment, or restart ownership.

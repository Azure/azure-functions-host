# WorkerProxy authentication and Token Service proxy

WorkerProxy requires TLS and a worker-specific caller JWT on its runtime-facing HTTP listener (28080) and FunctionRpc listener (50053). WorkerProxy returns a separate app-key proof during initialization on the same runtime-initiated duplex stream. Host must validate that proof before accepting Link or publishing the worker.

The loopback worker FunctionRpc listener (50054) remains plaintext and pod-local. Management on port 80 uses a separate container-management site-token contract, not the runtime caller JWT.

## Keys and identities

The platform sends the target WorkerProxy's `CONTAINER_ENCRYPTION_KEY` to Host as `runtimeAccessKey` in Link. Host retains that key by worker identity and uses it only for that worker's caller tokens. WorkerProxy validates them using its own startup container key, not a runtime or customer container key. There is no separate runtime-access-key environment setting.

Host reads its app name and `WEBSITE_AUTH_ENCRYPTION_KEY` through normal live specialization configuration. WorkerProxy reads the same app key from its startup configuration and takes the expected app name from its accepted assignment's `functionAppName`. Trusted app names are lowercased for the app audience, so a mixed-case runtime `WEBSITE_SITE_NAME` matches the same assigned app. Signed claim comparisons, worker identity and purposes remain exact. Assignment environment entries are not installed into the process or used as an app-key fallback. This does not filter or change customer app settings.

Both key types use the platform convention: 64-character hexadecimal, otherwise base64, with at least 32 decoded bytes. There is no fallback from a missing app key to a container/management key.

The protected WorkerProxy pod name is the Link route identity, not the customer worker's protobuf `worker_id`. The reverse proof includes that pod name as a signed claim and Host compares it ordinally with the expected route identity.

## Wire contract

`RuntimeWorkerAuthentication.cs` is shared source compiled into both sides. It centralizes the two directional token layouts, claims, signing, validation, reserved headers and protobuf-map entry.

| Direction | Carrier | Expected `aud` | Expected `purpose` | Additional required claim |
| --- | --- | --- | --- | --- |
| Host to WorkerProxy | `x-ms-functions-worker-token` HTTP header / gRPC metadata | `azure-functions-worker:<workerId>` | `worker` | None |
| WorkerProxy to Host | `WorkerInitResponse.capabilities["WorkerProxyAppToken"]` | `azure-functions-app:<appName>` | `app` | `worker_id: <workerId>` |

Both use HS256 with `typ: JWT`, integer UTC Unix timestamps `iat = nbf`, and `exp = iat + 300`. Validation allows 30 seconds of skew and rejects longer signed lifetimes.

`WorkerProxyAppToken` is a reserved entry in an existing protobuf map, not a new protobuf field or callback connection. This carrier remains subject to runtime/WorkerProxy review; implementing the key flow does not imply approval of the carrier. WorkerProxy discards any customer-supplied value before capability snapshotting and appends its own proof only to the runtime-facing transport clone. Valid success, failure and cancellation responses all carry the proof; authenticated failures retain their original diagnostics and cannot complete Link. WorkerProxy rejects missing results and unknown status values before signing. Host independently rejects those malformed results after validating and removing the proof, before logging capabilities, completing initialization or publishing the channel; Link returns retryable `503 WorkerUnavailable`. It is not an app capability, telemetry property or public state entry.

HTTP requests and newly opened gRPC calls need only the caller token, not an outbound app JWT. The runtime overwrites caller-supplied platform auth headers. WorkerProxy strips them before customer forwarding, including `x-ms-site-token` and the reserved legacy `x-ms-functions-app-token` header; ordinary customer `Authorization` remains intact.

## Management authentication and assignment

The compute Host's Link policy validates `x-ms-site-token` using **the runtime's own** `CONTAINER_ENCRYPTION_KEY`, even after specialization supplies an app key. All WorkerProxy management routes validate site tokens using **WorkerProxy's own** container key, independently of assignment, app-key availability and customer startup. Neither policy falls back to the app key or the other container's key. Ordinary WebHost authentication policies are unchanged.

Site tokens use HS256, issuer `https://legion.core.azurewebsites.net` and audience equal to the receiving platform pod name (`WEBSITE_POD_NAME`, falling back to `CONTAINER_NAME`). Issuer/audience matching and the five-minute lifetime tolerance follow the existing platform bearer rules. The platform normally backdates `nbf` one minute and sets `exp` ten minutes ahead; `iat` and the data-plane `purpose` claim are not required. Invalid/missing credentials or tokens return 401 before request processing. JWTs do not encrypt or bind the management request body.

Authentication diagnostics identify safe failure categories such as missing/invalid expected key, malformed token, signature, issuer, audience, purpose, worker identity and lifetime. They do not include token/key values or caller claim payloads, and external 401 responses remain generic. Compute management key configuration is checked at startup; WorkerProxy also flags unavailable startup app-key configuration without disabling management availability. Runtime app-key availability is required only when validating the initialization proof after specialization. Unrelated requests without a site-token header remain quiet; an actual protected-management challenge records the missing token.

`PUT /admin/worker/assignment` accepts validated configuration before any customer process, local gRPC connection or `StartStream`. Accepted app/group identity, environment retry identity, allocated `httpProxyEndpoint`, and requested `configLastModifiedTime`, `contentLastModifiedTime` and `maxHttpConcurrency` survive later attachments. All participate in immutable retry identity: matching retries return 204, changing any requested metadata or origin returns 409 without replacing the original assignment or revision, and a terminated assigned worker cannot be reused. These metadata values describe the requested assignment, not confirmation that versions are loaded or a concurrency limit is applied. Assignment acceptance is not customer initialization or Link success.

Requested version timestamps are signed 64-bit Unix epoch seconds; requested concurrency is a signed 32-bit integer, including zero. Numeric strings and JSON numbers normalize to the same typed metadata. Omission or null remains unspecified, distinct from an explicitly supplied zero. Malformed or out-of-range values are rejected through normal JSON binding; there are no additional positive-only or timestamp bounds.

`httpProxyEndpoint` is the actual allocated HTTP `IP:port`; WorkerProxy constructs HTTPS using that exact mapping. A compatible absolute HTTPS origin is also accepted. The field is separate from the local customer's destination. `WorkerProxy:HttpProxyEndpoint` remains an explicit trusted configuration alternative when assignment does not supply an origin; HTTP-capable initialization fails if neither is usable.

## Instance-state polling

The management availability/state endpoint is authenticated `POST /admin/infra/instanceState`. The initial platform request has an empty body and includes `x-ms-functions-pod-name`; JSON `null` is also accepted. Before assignment, the response is 204 with no fabricated app or platform identity. There are no separate `/admin/instance/ready`, `/admin/worker/ready` or `GET /admin/worker/state` routes.

After assignment, a 200 response is a flat origin-state snapshot with `functionsContainerType: "FunctionsSplitWorkerProxy"` first, followed by the accepted group/always-ready values, actual startup time, integer revision, status, link and health. The caller posts its last snapshot on the next poll. A snapshot matching the current known fields waits up to 60 seconds and returns 204; any changed known field returns the current snapshot. A higher cached revision or equal revision with an older startup time is a stale hint, not an invalid request. Negative/malformed revisions are rejected. Omitted/null runtime and extra unknown platform/version metadata do not cause false changes, and caller metadata is never installed or treated as Link acceptance.

The current handshake supplies neither full accepted runtime identity nor an authenticated acceptance acknowledgement to WorkerProxy. Therefore this auth-scoped implementation conservatively reports `Starting` and `Unlinked`, even after it has sent an app proof; it never equates assignment or `StartStream` with `Running`/`Linked`. Terminal assigned-session failures set `isUnhealthy`. Platform-owned `self` and confirmed loaded config/content version fields are omitted, not fabricated: requested timestamps are retained in assignment identity but are not evidence of applied versions. The platform decoder tolerates these omissions and reconstructs trusted pod identity, but its default version zero does **not** establish a matched application version. Full serving-state/runtime-identity/loaded-version publication is a separate integration requirement.

## Link and HTTP negotiation

The authenticated management route is `PUT /admin/workers/{workerId}`. Worker identity comes only from the path; body `workerPodName` is ignored. The existing platform payload is `{workerPodName, workerGrpcEndpoint, runtimeAccessKey}`. `workerGrpcEndpoint` is a scheme-less host:port, such as `100.86.1.2:48800`; Host constructs HTTPS. The mapped port must be explicit and between 1 and 65535. IPv4, DNS hosts, bracketed IPv6 and compatible absolute HTTPS authorities are supported. Plaintext URLs, whitespace, credentials, paths, query, fragment and percent escapes are invalid. There is no collection-root Link alias or Link HTTP endpoint.

HTTP support and routing remain negotiated through `HttpUri`. WorkerProxy validates the customer's local HTTP destination separately and advertises the accepted assignment's HTTPS origin (or explicit trusted configuration alternative). If an HTTP-capable worker has no usable external origin, initialization fails instead of forwarding a local/customer address or guessing an allocated port. Link may wait for a still-starting customer worker within the existing bounded initialization attempt; only successful initialization and valid app proof publish a worker.

The Link budget derives from the same worker timing options used by the linked channel: the five-second connection budget plus `ProcessStartupTimeout` and `InitializationTimeout` (defaults 60 and 10 seconds, totaling 75). Standard options configuration under `RpcClient:Worker` can set those timeouts and `EnvironmentReloadTimeout`; values must remain positive and bounded. A retry or canceled caller does not create an unbounded wait.

When the Token Service proxy is enabled, its opt-in `Functions:TokenServiceProxy:LinkTimeout` is the independent registry-owned early-link deadline (default two minutes). The channel's startup and initialization waits use that deadline so identity bootstrap can finish before the customer starts. Canceling an administrative caller ends only its wait; explicit unlink, shutdown or the bounded deadline ends the pending attempt. With the proxy disabled, the shared worker-derived budget above is unchanged.

If a runtime disconnects before it has sent any protocol message to the customer worker, WorkerProxy keeps the accepted assignment and the same live local stream. A newly authenticated runtime can receive the cached validated `StartStream` and initialize normally. Canceled-runtime generations are fenced so late input cannot reach the replacement. **Once any runtime-to-worker protocol traffic begins, loss remains terminal**: retrying an in-flight initialization or reusing an initialized worker is not supported. Genuine customer failure, shutdown and terminal assignment protection are unchanged. This is narrowly pre-initialization recovery, not general reconnect or worker-reuse support.

Token Service bootstrap controls are consumed by the infrastructure bridge, not forwarded as customer worker protocol input, so they can run before `StartStream` and do not by themselves cross that retry boundary. Each runtime generation owns its tracked proxy writes and pending HTTP outcomes. A detached generation's requests fail rather than being replayed onto its replacement; the next authenticated stream negotiates a fresh proxy session while the same pre-init local worker remains available.

Capability reloads are rewritten to the same configured proxy origin. Raw customer origins and customer-supplied authentication proofs are not forwarded as runtime destinations. Host rejects a changed initialized proxy origin before it can receive caller credentials.

Invalid advertised HTTP capabilities, including malformed and unsafe absolute origins, fail Link with an explicit retryable `503 WorkerUnavailable`; they are not treated as programming errors or successful fallback. The expiry-only HTTP transport is owned by a dedicated root-lifetime compute component and supplied only to linked WorkerProxy channels. It does not replace ordinary or custom `IHttpProxyService` registrations, and its handler is disposed with its owning root provider.

The checked-in combined Host/WorkerProxy integration fixture leaves the startup proxy-origin option absent, assigns a live scheme-less mapping, and uses distinct external/listener ports for both HTTPS and gRPC. Its local TCP mappings preserve TLS rather than replacing it. The fixture initializes through the real MVC Link and registry-owned worker setup, invokes once, performs a real environment reload with a changed customer-local path, and invokes again. Both forwarding calls must use the assigned HTTPS origin and mapped port, while customer authorization is preserved and platform headers/proof entries remain stripped. The same fixture can target a freshly published native WorkerProxy with explicitly supplied mapped and listener addresses.

## Startup-provisioned TLS certificate

The image startup script must generate a fresh self-signed PEM certificate and unencrypted PEM private key before starting WorkerProxy. The serving identity must not be baked into an image.

| Option | Environment variable | Default file |
| --- | --- | --- |
| `WorkerProxy:CertificatePath` | `WORKERPROXY__CERTIFICATEPATH` | `/etc/ssl/workerproxy/server.crt` |
| `WorkerProxy:CertificateKeyPath` | `WORKERPROXY__CERTIFICATEKEYPATH` | `/etc/ssl/workerproxy/server.key` |

WorkerProxy loads those files for both runtime-facing TLS listeners. Missing, malformed or mismatched files fail startup. There is no C# certificate generation or plaintext fallback.

The WorkerProxy-specific Host HTTP/gRPC clients share an expiry-only UTC certificate callback: absent or expired certificates are rejected, but chain, issuer, hostname and pin are not checked. Ordinary Host outbound clients and WorkerProxy's customer-facing forwarding client retain their normal validation.

## Release conditions and limitations

WorkerProxy consumes the startup app key and assignment-carried mapped origin; matching platform provisioning and published images must still be verified before rollout. Management availability, assignment acceptance, successful Link and full platform serving-state convergence are distinct milestones. The state/publication gap above must be resolved before claiming full serving convergence.

The app proof demonstrates app-key possession and worker association, not a uniquely trusted WorkerProxy process. Certificate expiry checking does not establish server identity. Protected key-bearing management delivery, worker-key isolation and cross-app containment remain release conditions; a site token does not provide management payload confidentiality or integrity.

Caller JWTs and returned proofs are not bound to a TLS session and can be replayed within their five-minute lifetime plus the 30-second clock tolerance. Expiry is checked at call admission; it does not revoke or renew open streams. No TLS-session binding, nonce or new token carrier is added. General worker reuse, in-place key rotation, stream renewal and mTLS are outside this implementation.
## Token Service proxy configuration

The compute-separated `Functions.Host` and `Functions.WorkerProxy` can forward managed-identity HTTP requests over their existing Host-initiated gRPC stream. The Token Service runs only in the Host environment; no worker-pod Token Service or additional cross-pod Token Service listener is needed. The feature is **disabled by default** and must be enabled on both products. These settings supplement existing worker registration, listener, and Host-link configuration.

### Required environment

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

### Limits and timeouts

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

### Startup and failure behavior

Identity calls can precede customer `StartStream`/`WorkerInit`. Waiting for a transport, Host preparation, HTTP forwarding, and local response delivery share one request deadline. Canceling an administrative link HTTP caller ends only that caller's wait; the registry retains its bounded link attempt. Proxy Hello/Ready is not worker initialization or upstream health.

Saturation returns `429` with `Retry-After: 1`, without a capacity wait queue. Local oversized bodies/headers return `413`/`431`; upstream transport/response-limit failures return `502`; unavailable transport or prepared-but-invalid Host configuration returns `503`; deadline expiry returns `504`. Upstream HTTP statuses and retry headers otherwise pass through. Cancellation retains capacity until terminal acknowledgment or transport cleanup; queued terminal responses retain Host reservations until physical write/discard.

Automatic upstream HTTP-client URI logging is disabled. With the proxy enabled, WorkerProxy suppresses request-URI logs below Warning in `Microsoft.AspNetCore.Hosting.Diagnostics`, including provider-specific overrides, on all its listeners. Stricter filters and feature-off logging are unchanged. Do not log credentials, customer queries, or token bodies elsewhere.

For a local mock-backed run:

```powershell
dotnet run --project tools\ComputeSeparation\AppHost\ComputeSeparation.AppHost.csproj --launch-profile project-identity
```

See the [compute-separation harness guide](../../tools/ComputeSeparation/README.md#debug-the-complete-first-start-flow) for mock controls and debugging. Production requires the approved authenticated Host/WorkerProxy transport, platform settings, and authorization/revocation integration. Real Token Service interoperability, all-language qualification, and persistent transport across customer-only restarts remain separate qualification/lifecycle work; this feature does not redesign registration, assignment, or restart ownership.

# Runtime connection authentication

WorkerProxy requires TLS and a worker-specific caller JWT on its runtime-facing HTTP listener (28080) and FunctionRpc listener (50053). WorkerProxy returns a separate app-key proof during initialization on the same runtime-initiated duplex stream. Host must validate that proof before accepting Link or publishing the worker.

The loopback worker FunctionRpc listener (50054) and management listener retain their separate responsibilities. They do not use these runtime caller tokens.

## Keys and identities

The platform sends the target WorkerProxy's `CONTAINER_ENCRYPTION_KEY` to Host as `runtimeAccessKey` in Link. Host retains that key by worker identity and uses it only for that worker's caller tokens. WorkerProxy validates them using its own startup container key, not a runtime or customer container key. There is no separate runtime-access-key environment setting.

Host reads its app name and `WEBSITE_AUTH_ENCRYPTION_KEY` through normal live specialization configuration. WorkerProxy reads its already-provisioned app key through normal configuration and takes the expected app name from its accepted assignment's `functionAppName`. Assignment environment entries are not installed into the process or used as an app-key fallback. This does not filter or change customer app settings.

Both key types use the platform convention: 64-character hexadecimal, otherwise base64, with at least 32 decoded bytes. There is no fallback from a missing app key to a container/management key.

The protected WorkerProxy pod name is the Link route identity, not the customer worker's protobuf `worker_id`. The reverse proof includes that pod name as a signed claim and Host compares it ordinally with the expected route identity.

## Wire contract

`RuntimeWorkerAuthentication.cs` is shared source compiled into both sides. It centralizes the two directional token layouts, claims, signing, validation, reserved headers and protobuf-map entry.

| Direction | Carrier | Expected `aud` | Expected `purpose` | Additional required claim |
| --- | --- | --- | --- | --- |
| Host to WorkerProxy | `x-ms-functions-worker-token` HTTP header / gRPC metadata | `azure-functions-worker:<workerId>` | `worker` | None |
| WorkerProxy to Host | `WorkerInitResponse.capabilities["WorkerProxyAppToken"]` | `azure-functions-app:<appName>` | `app` | `worker_id: <workerId>` |

Both use HS256 with `typ: JWT`, integer UTC Unix timestamps `iat = nbf`, and `exp = iat + 300`. Validation allows 30 seconds of skew and rejects longer signed lifetimes.

`WorkerProxyAppToken` is a reserved entry in an existing protobuf map, not a new protobuf field or callback connection. WorkerProxy discards any customer-supplied value before capability snapshotting and appends its own proof only to the runtime-facing transport clone. Host validates and removes it before logging capabilities, completing initialization or publishing the channel. It is not an app capability, telemetry property or public state entry.

HTTP requests and newly opened gRPC calls need only the caller token, not an outbound app JWT. The runtime overwrites caller-supplied platform auth headers. WorkerProxy strips them before customer forwarding, including the reserved legacy `x-ms-functions-app-token` header; ordinary customer `Authorization` remains intact.

## Link and HTTP negotiation

The management route is `PUT /admin/workers/{workerId}`. Worker identity comes only from the path; body `workerPodName` is ignored. The existing platform payload is `{workerPodName, workerGrpcEndpoint, runtimeAccessKey}`. The gRPC endpoint must be an HTTPS authority without credentials, path prefix, query or fragment. There is no collection-root Link alias or Link HTTP endpoint.

HTTP support and routing remain negotiated through `HttpUri`. WorkerProxy validates the customer's local HTTP destination separately and advertises its configured `WorkerProxy:HttpProxyEndpoint`, which must be the actual reachable HTTPS forwarding origin. If an HTTP-capable worker has no usable external origin, initialization fails instead of forwarding a local/customer address or guessing an allocated port.

Capability reloads are rewritten to the same configured proxy origin. Raw customer origins and customer-supplied authentication proofs are not forwarded as runtime destinations. Host rejects a changed initialized proxy origin before it can receive caller credentials.

## Startup-provisioned TLS certificate

The image startup script must generate a fresh self-signed PEM certificate and unencrypted PEM private key before starting WorkerProxy. The serving identity must not be baked into an image.

| Option | Environment variable | Default file |
| --- | --- | --- |
| `WorkerProxy:CertificatePath` | `WORKERPROXY__CERTIFICATEPATH` | `/etc/ssl/workerproxy/server.crt` |
| `WorkerProxy:CertificateKeyPath` | `WORKERPROXY__CERTIFICATEKEYPATH` | `/etc/ssl/workerproxy/server.key` |

WorkerProxy loads those files for both runtime-facing TLS listeners. Missing, malformed or mismatched files fail startup. There is no C# certificate generation or plaintext fallback.

The WorkerProxy-specific Host HTTP/gRPC clients share an expiry-only UTC certificate callback: absent or expired certificates are rejected, but chain, issuer, hostname and pin are not checked. Ordinary Host outbound clients and WorkerProxy's customer-facing forwarding client retain their normal validation.

## Release conditions and limitations

The code does not establish the platform delivery path for WorkerProxy's already-provisioned app key or its actual mapped HTTPS HTTP origin. Matching configuration and image startup integration must be verified before release; neither is silently replaced with assignment injection or an untrusted destination.

The app proof demonstrates app-key possession and worker association, not a uniquely trusted WorkerProxy process. Certificate expiry checking does not establish server identity. Correct endpoint authorization, protected key-bearing management delivery, worker-key isolation and cross-app containment remain release conditions.

Expiry is checked at call admission; it does not revoke or renew open streams. Worker reuse, in-place key rotation, stream renewal and mTLS are outside this implementation.

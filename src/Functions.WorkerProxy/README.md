# Runtime connection authentication

WorkerProxy requires TLS and two JWTs on its runtime-facing HTTP listener (28080) and FunctionRpc listener (50053). Both tokens are checked before admitting an HTTP request or opening a gRPC stream. Missing or invalid authentication returns HTTP 401 or gRPC `Unauthenticated`.

The loopback worker FunctionRpc listener (50054) and the management listener retain their separate responsibilities. They do not require these runtime tokens.

## Keys and trusted identities

The platform supplies the per-worker base64 `FUNCTIONS_WORKER_RUNTIME_ACCESS_KEY` to WorkerProxy at startup and sends the same value to Functions Host in `runtimeAccessKey` on `PUT /admin/workers/{workerId}`. This key is independent of management credentials.

The Host reads `WEBSITE_AUTH_ENCRYPTION_KEY` and `WEBSITE_SITE_NAME` through its existing live specialization configuration. WorkerProxy obtains the same app key from its accepted management assignment's `environment.WEBSITE_AUTH_ENCRYPTION_KEY`, and the expected app name from `functionAppName`. Assignment authentication configuration remains private: it is not installed in the proxy process environment, forwarded to the customer worker, or published in state responses.

This does not filter or change customer app settings delivered through the Host's normal specialization path.

The Link route's `workerId` must be the protected WorkerProxy pod name. It is not selected from JWT claims. WorkerProxy uses its platform-provided pod name when checking the worker audience.

Worker keys are base64. App keys follow the existing platform convention: 64-character hexadecimal, otherwise base64. Decoded keys must contain at least 32 bytes. There is no fallback to a container-management key, and an unassigned proxy cannot accept runtime traffic.

## Wire contract

`RuntimeWorkerAuthentication.cs` is shared source compiled into the runtime client and WorkerProxy. It centralizes header names, audiences, purposes, key decoding, signing, validation, lifetime and skew without reflection-based JSON serialization.

| Token | HTTP header / gRPC metadata | Expected `aud` | Expected `purpose` |
| --- | --- | --- | --- |
| Worker | `x-ms-functions-worker-token` | `azure-functions-worker:<workerId>` | `worker` |
| App | `x-ms-functions-app-token` | `azure-functions-app:<appName>` | `app` |

Tokens use HS256 with `typ: JWT` and integer UTC Unix timestamps `iat`, `nbf` and `exp`. Issuance and not-before timestamps match; expiry is five minutes after issuance. Validation allows 30 seconds of clock skew and rejects a signed lifetime longer than five minutes. Both signatures, audiences, purposes and lifetimes must be valid.

The runtime issues fresh tokens for each HTTP invocation and newly opened gRPC call. Caller-supplied platform token headers are overwritten. WorkerProxy removes both platform token headers before forwarding customer HTTP traffic; the customer's `Authorization` header is preserved. Rejected authentication is logged without tokens, key values or assignment environment entries.

## HTTPS endpoints and HTTP capability negotiation

Link requires an HTTPS authority in `workerGrpcEndpoint`. Its optional `workerHttpEndpoint` must also be an HTTPS authority; it is required if the worker negotiates HTTP support. Neither endpoint may contain credentials, a path prefix, a query or a fragment.

The Host pins HTTP forwarding to the origin supplied by Link. A worker capability cannot replace that origin, including during capability reload. An HTTP-capable worker without an HTTP Link origin fails initialization rather than guessing a port or falling back to an untrusted destination.

WorkerProxy still validates and probes the customer worker's local HTTP destination. When an external mapped port is unavailable at proxy startup, `HttpProxyEndpoint` may be omitted: the local `HttpUri` is retained only as an HTTP-support signal, and the Host substitutes its protected Link origin before using the capability.

## TLS and limitations

Each proxy process generates and owns its self-signed certificate and private key. Both runtime-facing listeners use that certificate. Certificates and private keys are not shared or baked into an image.

Only the compute-separated runtime's WorkerProxy HTTP/gRPC clients accept arbitrary server certificates. Ordinary Host outbound clients and WorkerProxy's customer-facing forwarding client retain their normal certificate validation.

**This does not authenticate the server's identity.** Deployment must protect key-bearing Link and assignment delivery, authorize the endpoint supplied to each runtime, keep worker keys unavailable to customer code, and establish cross-app containment before release. The tokens prove possession of worker and app keys, not a unique runtime identity.

JWT expiry is checked at call admission. It does not renew, revoke or close an already-open stream. Worker reuse, in-place key rotation, stream renewal and mutual TLS are not implemented by this protocol.

## Why

Compute separation removes the host-side gRPC listener that WebJobs extension
clients currently call. Extension calls must instead terminate at the worker
proxy and traverse host-initiated extension streams without changing the
worker-facing language protocol or extension registration API.

## What Changes

- Keep host-to-proxy and worker-to-proxy language traffic on the existing
  `FunctionRpc` contract.
- Add a dedicated extension RPC stream carrying lifecycle messages for start,
  data, half-close, cancellation, response headers, completion, and flow control.
- Allow the proxy to translate ordinary worker extension gRPC calls into
  multiplexed messages on one fixed physical stream per worker channel.
- Keep the wire contract shard-ready for future expansion while the runtime
  reconnects, rather than scales out, the single current stream.
- Preserve ScriptHost-scoped endpoint routing by rebinding new calls to the
  replacement ScriptHost while in-flight calls remain pinned to their original
  endpoint catalog.

## Capabilities

### New Capabilities

- `extension-proxy-rpc`: Defines a dedicated, multiplexed extension RPC stream
  contract while retaining `FunctionRpc` for language traffic.

### Modified Capabilities

None.

## Impact

- Adds a host-owned protobuf contract compiled by `WebJobs.Script.Grpc` and
  `Functions.WorkerProxy`.
- Extends the host outbound gRPC client and proxy with independent extension
  streams alongside the existing language stream.
- Does not change worker-facing `FunctionRpc` wire behavior.
- Requires new protocol compatibility tests and generated client/server types.

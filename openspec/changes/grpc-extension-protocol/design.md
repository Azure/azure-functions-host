## Context

The proxy exposes `FunctionRpc.EventStream` on separate ports for the host and
worker and relays `StreamingMessage` values between them. Extension traffic
must remain separate so the language stream is not overloaded with multiplexed
logical calls and worker SDKs do not need to understand extension envelopes.

The worker extension client should instead continue issuing ordinary gRPC
calls to a proxy endpoint. The proxy will translate those calls into logical
extension messages on the host-facing stream.

## Goals / Non-Goals

**Goals:**

- Establish a host-owned extension wire contract for host-to-proxy communication.
- Preserve worker-facing `FunctionRpc` compatibility.
- Keep existing `StreamingMessage` values on `FunctionRpc` and carry extension
  call events on one independent extension stream.
- Define protocol messages for all four gRPC cardinalities.
- Generate client and server types in the host and proxy projects.

**Non-Goals:**

- Implement the proxy's generic extension gRPC listener in the protocol slice.
- Dispatch extension calls to ScriptHost endpoints in the protocol slice.
- Change the language-worker protobuf subtree.
- Wire the host and proxy to the new service before compatibility tests exist.

## Decisions

### Reuse FunctionRpc for language traffic

The host and worker continue using `FunctionRpc.EventStream` on their respective
proxy ports. No wrapper is required because language messages do not share a
stream with extension calls.

Add a host-owned `ExtensionRpc.EventStream` carrying extension lifecycle
messages. The runtime opens exactly one extension stream at a time on the same
gRPC channel used by its `FunctionRpc` client.

Adding extension fields directly to `StreamingMessage` was rejected because it
places a runtime-to-proxy concern in the shared language-worker protocol.

### Model extension calls as lifecycle events

Every extension event contains a call identifier and one event payload:

- protocol hello and readiness
- call start
- bounded data chunk
- request half-close
- cancellation
- response headers
- completion status and trailers
- flow-control window update

The outer stream preserves event order. Call identifiers permit events for
different logical calls to interleave.

### Represent metadata without HTTP-specific framing

Metadata entries contain a key and raw bytes. This supports ASCII and binary
gRPC metadata without requiring the extension stream to reproduce HTTP/2 header
compression or framing.

### Keep status codes transport-neutral

Completion uses a protocol enum corresponding to standard gRPC status codes,
plus detail and trailers. Generated code must not depend on `Grpc.Core.Status`
types in the contract model.

### Compile the contract independently in host and proxy

The protobuf source lives outside the language-worker protobuf subtree. The
host project generates client and server types; the AOT proxy generates the
server/client types it needs from the same source file.

### Accept extension calls through a schema-independent gRPC ingress

The proxy reserves the exact worker-facing `FunctionRpc.EventStream` route for
the language relay. Other HTTP/2 `application/grpc` requests on the worker gRPC
port are handled by a generic extension ingress.

The ingress does not register extension service descriptors and does not
deserialize application protobuf messages. It parses only gRPC's transport
framing: the compression flag, four-byte message length, and opaque message
bytes. Each data event carries the complete message length in addition to chunk
offsets, allowing the receiver to write the gRPC prefix before streaming chunks
without buffering the entire message. The method path, metadata, deadline,
message boundaries, compression flags, response headers, final status, and
trailers are translated to and from extension lifecycle events.

This preserves existing generated extension clients: they connect to the same
worker-facing proxy endpoint and issue ordinary gRPC calls using their existing
contracts.

### Isolate extension traffic with one reconnecting stream

There is one `FunctionRpc.EventStream` for language-worker traffic and exactly
one active `ExtensionRpc.EventStream` for extension calls per worker channel.
The extension stream has an independent reader, serialized writer queue,
HTTP/2 stream flow-control window, call registry, and lifecycle. It uses the
runtime's existing gRPC channel.

The proxy assigns every logical call to the current ready stream and pins it
there until completion. Calls never migrate. A stream failure cancels its calls
without interrupting the language-worker relay. While that worker channel
remains alive, the runtime opens one replacement stream after a bounded retry
delay.

The current implementation does not open additional streams based on active
calls, queue depth, queued bytes, or latency, and has no least-loaded assignment,
scale-out, or scale-down behavior. The `shard_id` wire field is retained to keep
the protocol capable of future multi-stream expansion.

The proxy does not impose a fixed concurrent-call limit. Rejecting otherwise
valid worker calls would turn stream saturation into an application-visible
failure without evidence that a second stream is needed. Instead, structured
call-open and call-completion events correlate active-call counts with call-open
latency and total duration. Those measurements will determine whether future
sharding complexity is justified.

Individual calls never write directly to the gRPC stream. The stream owns one
bounded outbound queue and is the only component that invokes its stream
writer. Per-call credits prevent a congested call from filling bounded queues.

### Resolve host endpoints behind a routing abstraction

The host call dispatcher depends on an endpoint-router abstraction rather than
the current globally active endpoint data source. The ScriptHost-scoped catalog
work supplies that abstraction later. This permits bridge and framing behavior
to be tested independently without temporarily routing calls through the wrong
ScriptHost generation.

External worker channels are owned by the WebHost and reused across ScriptHost
generations. When a replacement ScriptHost registers its catalog, new calls on
an existing worker channel are rebound to that catalog. Calls already dispatched
retain a lease on the original catalog and service provider until they complete
or the original ScriptHost begins stopping. ScriptHost stop blocks new leases,
cancels any remaining calls, and waits for those leases before disposal.

## Risks / Trade-offs

- **[Risk] A new service requires a coordinated host/proxy rollout** ->
  Keep language traffic on the existing `FunctionRpc` endpoint so unsupported
  extension streams do not affect worker communication.
- **[Risk] The first protocol shape may omit streaming state** ->
  Define all lifecycle and flow-control messages before wiring unary calls.
- **[Risk] Generated proxy code may break Native AOT** ->
  Build the proxy and run its existing AOT-focused tests after adding the proto.
- **[Risk] A catch-all route captures language-worker traffic** ->
  Reserve the exact `FunctionRpc.EventStream` route and test that it takes
  precedence over generic extension ingress.
- **[Risk] Concurrent producers corrupt an extension stream** ->
  Give the extension stream an exclusive serialized writer.
- **[Risk] A busy extension call delays invocation traffic** ->
  Keep extension traffic off `FunctionRpc.EventStream` and enforce per-call
  credits and bounded queues.
- **[Risk] Stream replacement or call migration breaks ordering** ->
  Permanently pin calls and cancel them when their physical stream fails.

## Migration Plan

1. Add the new protobuf contract and generated-code configuration.
2. Add serialization and service-shape tests.
3. Open the extension stream alongside the existing host `FunctionRpc` stream.
4. Add the proxy extension listener and host dispatcher.

Rollback disables the extension stream; both host-facing and worker-facing
`FunctionRpc` behavior remains unchanged.

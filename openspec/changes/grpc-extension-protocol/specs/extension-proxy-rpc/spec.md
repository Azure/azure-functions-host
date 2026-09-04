## Purpose

Defines host-to-worker-proxy extension RPC streams that stay isolated from
the existing `FunctionRpc` language streams.

## ADDED Requirements

### Requirement: Existing language stream contract
The host and worker SHALL continue using the existing bidirectional
`FunctionRpc.EventStream` contract on their respective proxy ports.

#### Scenario: Runtime establishes its stream
- **WHEN** the Functions host connects to a worker proxy
- **THEN** it uses `FunctionRpc.EventStream`
- **AND** the worker continues using the existing `FunctionRpc` contract

### Requirement: Language-worker message relay
The proxy SHALL relay existing language-worker streaming messages without
changing their serialized contents.

#### Scenario: Host sends a worker message
- **WHEN** the host sends a language-worker message through `FunctionRpc`
- **THEN** the proxy relays the original message to the worker

#### Scenario: Worker sends a host message
- **WHEN** the proxy receives a language-worker message from the worker
- **THEN** the proxy relays the original message to the host

### Requirement: Fixed extension call stream
The system SHALL provide a dedicated `ExtensionRpc.EventStream` contract that
the runtime opens exactly once at a time per worker channel, independently from
`FunctionRpc.EventStream`. The runtime SHALL reconnect a failed extension stream
with a bounded retry delay while the worker channel remains alive.

#### Scenario: Concurrent calls share the stream
- **WHEN** messages from multiple extension calls are interleaved
- **THEN** each message is correlated to its logical call identifier
- **AND** ordering is preserved within each logical call

#### Scenario: Extension load increases
- **WHEN** active calls, queue pressure, or write latency increases
- **THEN** the runtime keeps exactly one extension stream open
- **AND** it does not open another stream or redistribute calls
- **AND** the proxy does not reject calls based on a fixed concurrency limit
- **AND** call-open latency and duration are recorded with active-call counts

#### Scenario: Extension stream fails
- **WHEN** the extension stream fails while the worker channel remains alive
- **THEN** its logical calls are cancelled
- **AND** the runtime opens one replacement stream after a bounded retry delay
- **AND** the independent language-worker relay remains connected

#### Scenario: Call is assigned
- **WHEN** the proxy assigns an extension call to the current physical stream
- **THEN** every lifecycle event for that call remains on that stream
- **AND** the call is never migrated to another physical stream
- **AND** `shard_id` identifies the physical stream for wire compatibility and
  future multi-stream expansion

### Requirement: Complete extension call lifecycle
The extension protocol SHALL represent call start, request data, request
half-close, cancellation, response headers, response data, completion status,
trailers, and flow-control updates.

#### Scenario: Request is half-closed
- **WHEN** the proxy reports that a worker extension client has finished sending
  request messages
- **THEN** the host observes a half-close without cancelling the response side

#### Scenario: Call is cancelled
- **WHEN** either side cancels an extension call
- **THEN** the peer can identify the affected call and terminate both directions

#### Scenario: Call completes
- **WHEN** an extension endpoint completes
- **THEN** the proxy receives the final status and trailers for the associated
  call

### Requirement: Streaming flow control
The extension protocol SHALL provide bounded data chunks and per-call
flow-control credits.

#### Scenario: Call exhausts its window
- **WHEN** a sender consumes all credits for a logical call
- **THEN** it stops sending data for that call until the peer returns credits
- **AND** unrelated calls remain eligible to send

### Requirement: Protocol compatibility
The runtime and proxy SHALL negotiate a supported extension protocol version
before extension calls are accepted.

#### Scenario: Compatible version
- **WHEN** the runtime and proxy share a supported protocol version
- **THEN** they select that version and enable extension call messages

#### Scenario: Incompatible version
- **WHEN** the runtime and proxy have no supported protocol version in common
- **THEN** language-worker relay remains available
- **AND** extension calls are rejected explicitly

### Requirement: Worker-facing compatibility
The protocol change SHALL NOT require language workers to send or receive the
runtime-to-proxy envelope.

#### Scenario: Existing worker connects
- **WHEN** a language worker connects to the proxy
- **THEN** its `FunctionRpc` stream behavior remains unchanged

### Requirement: Opaque extension gRPC ingress
The proxy SHALL accept ordinary extension gRPC calls on the worker-facing gRPC
port without requiring extension service descriptors or application protobuf
types.

#### Scenario: Existing extension client invokes a method
- **WHEN** a worker extension client calls an arbitrary gRPC method other than
  the reserved `FunctionRpc` service
- **THEN** the proxy preserves the method path, metadata, message boundaries,
  compression flags, deadline, status, and trailers
- **AND** translates the call to extension lifecycle messages without
  deserializing its application payloads

#### Scenario: Worker opens its language stream
- **WHEN** the worker calls the exact `FunctionRpc.EventStream` route
- **THEN** the request is handled by `FunctionRpcRelay`
- **AND** it is never captured by the generic extension ingress

### Requirement: Serialized writers per physical stream
Language-worker relay messages SHALL use `FunctionRpc.EventStream`, while
extension lifecycle messages SHALL use the single active
`ExtensionRpc.EventStream`.

#### Scenario: Concurrent producers send extension messages
- **WHEN** calls assigned to the extension stream produce outbound messages concurrently
- **THEN** messages are written through the stream's serialized outbound queue
- **AND** no gRPC stream writer is invoked concurrently

#### Scenario: Extension stream is congested
- **WHEN** the extension stream is backpressured
- **THEN** language-worker messages continue on the independent `FunctionRpc` stream
- **AND** per-call credits and bounded queues prevent one logical call from
  consuming unbounded capacity

### Requirement: ScriptHost-scoped extension routing
The host SHALL maintain an independent extension endpoint catalog for each
ScriptHost generation.

#### Scenario: Replacement ScriptHost starts
- **WHEN** a replacement ScriptHost registers its extension endpoint catalog
- **THEN** new extension calls from existing worker channels resolve against the
  replacement catalog
- **AND** calls already dispatched remain pinned to their original endpoint
  catalog and service provider

#### Scenario: Previous ScriptHost stops
- **WHEN** a previous ScriptHost begins stopping
- **THEN** its catalog accepts no new call leases
- **AND** its remaining calls are cancelled and awaited before its service
  provider is disposed

#### Scenario: Worker channel closes
- **WHEN** an external worker channel closes
- **THEN** its endpoint-catalog binding is removed
- **AND** later extension calls for that worker identifier are rejected

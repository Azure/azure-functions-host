## 1. Extension RPC Protocol

- [x] 1.1 Add the host-owned extension RPC service and complete extension RPC lifecycle messages
- [x] 1.2 Configure the host gRPC and worker proxy projects to generate the new protocol types without modifying the language-worker protobuf subtree
- [x] 1.3 Add protocol tests covering extension call correlation, metadata, completion, and flow-control serialization

## 2. Independent Language and Extension Streams

- [x] 2.1 Keep host and worker language traffic on the existing port-aware `FunctionRpc` relay
- [x] 2.2 Open a dedicated extension stream alongside the host outbound `FunctionRpc` client
- [x] 2.3 Add end-to-end relay tests proving worker-facing `FunctionRpc` behavior remains unchanged

## 3. Extension Call Bridge

- [x] 3.1 Add the dedicated extension stream and proxy generic gRPC ingress while reserving the exact worker `FunctionRpc` route
- [x] 3.2 Add host extension stream coordination and logical-call dispatch behind an endpoint-router abstraction
- [x] 3.3 Implement fixed-stream call pinning, schema-independent framing, bounded per-call flow control, response reconstruction, and cancellation propagation

## 4. ScriptHost Routing and Lifecycle

- [x] 4.1 Add ScriptHost-scoped endpoint catalogs and generation-aware worker routing
- [x] 4.2 Keep draining ScriptHost endpoint catalogs reachable until disposal completes
- [x] 4.3 Add overlapping ScriptHost generation and shutdown routing tests

## 5. Fixed Stream Design Revision

- [x] 5.1 Replace adaptive multi-shard behavior with one reconnecting extension stream per worker channel, retain shard-ready wire fields, and harden ingress, drain rebinding, timeout, and terminal-write cleanup paths

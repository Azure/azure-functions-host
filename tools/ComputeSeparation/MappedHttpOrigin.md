# WorkerProxy assigned HTTP origin

The platform supplies `httpProxyEndpoint` as an optional string on
`PUT /admin/worker/assignment` (Legion's `FunctionsWorkerProxyAssignmentRequest`).
HNA sets the actual absolute `http://{mapped endpoint}` immediately before sending
the assignment, after the external mapping is known. Initial RCP construction
leaves it null. It is **not** a container creation/startup setting.

Example addition to an otherwise unchanged assignment:

```json
{ "httpProxyEndpoint": "http://mapped-worker.example:49187/" }
```

All existing fields, including `startupMode`, retain their contracts. A supplied
origin must be absolute HTTP(S), with a concrete host, nonzero port, root path,
and no credentials, query, or fragment. Empty, whitespace, wildcard, and malformed
values return `400 InvalidValue` targeting `httpProxyEndpoint` without claiming
assignment. Null/omitted remains valid for non-HTTP workers.

The accepted string is immutable assignment identity. Identical retries return
204; a changed string (including null versus non-null) returns 409, using the
same ordinal comparison as the other assignment strings. It is not applied to
the worker environment or published in the management state response.

When a worker advertises `HttpUri`, Proxy retains the real worker-local destination
(or explicit `WorkerHttpEndpoint` override) for forwarding and rewrites only the
outgoing capability to the assigned mapped origin. A missing assigned endpoint
fails HTTP initialization, even if legacy startup configuration exists. No inferred
mapping, gRPC-port-plus-one, post-start environment mutation, or silent transport
fallback is used. Same-origin destinations fail to prevent forwarding loops.
Workers without `HttpUri` retain non-HTTP behavior.

## Ordering and compatibility

The platform must wait for WorkerProxy assignment to succeed **before linking**
the runtime. `WorkerChannelRegistry` connects the outbound gRPC transport and
starts `RpcClientWorkerChannel`; `WorkerChannel` sends `WorkerInitRequest` as soon
as it reads the relayed `StartStream`. The Proxy reader finalizes the first
successful `WorkerInitResponse` immediately, then freezes capabilities and publishes
the real HTTP destination before forwarding the response. It does not wait for a
later assignment. Missing assignment/origin therefore fails HTTP initialization.

For compatibility, the preexisting explicit `WorkerProxy:HttpProxyEndpoint`
configuration remains available **only to unassigned legacy sessions** (including
the container-only local harness). It is never a fallback for an accepted assignment.
The project harness now includes Aspire's actual allocated endpoint in its assignment.
Coordinate Legion producer and Host consumer rollout: an older consumer may ignore
the additive field, while an older producer that omits it cannot initialize an
assigned HTTP worker. `startupMode` is unchanged.

Focused tests cover management serialization/validation, endpoint retry identity,
missing endpoint versus non-HTTP behavior, assigned capability propagation,
real local Proxy-to-Worker HTTP execution, and existing legacy/Host capability
consumption. These are not one combined cross-repo E2E, deployment, or private-stamp
validation.

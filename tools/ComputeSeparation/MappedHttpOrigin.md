# WorkerProxy assigned HTTP origin

The platform supplies `httpProxyEndpoint` as an optional **scheme-less host:port** string on
`PUT /admin/worker/assignment`. Legion carries its allocated `28080` mapping from HNA in
`PodSpecializationDescriptor.WorkerHttpEndpoint`; NNA copies the typed `PodIpv4Endpoint?`
into `FunctionsWorkerProxyAssignmentContext.HttpProxyEndpoint`. It serializes as
`<ip>:<port>` after allocation. It is not a container creation/startup setting.

Example addition to an otherwise unchanged assignment:

```json
{ "httpProxyEndpoint": "100.86.0.4:49123" }
```

All existing fields, including `startupMode`, retain their contracts. A supplied
endpoint requires a concrete host and an explicit decimal port from 1 through 65535.
IPv6 hosts must be bracketed, for example `[::1]:49123`. Schemes, paths (including a
trailing slash), credentials, queries, fragments, percent escapes, whitespace, wildcard
hosts, missing ports, and malformed values return `400 InvalidValue` targeting
`httpProxyEndpoint` without claiming assignment. Null/omitted remains valid for non-HTTP workers.

WorkerProxy derives the advertised URI scheme from
`WorkerProxyEndpointConfiguration.GetHttpAddress().Scheme`: HTTP in the pre-TLS image,
HTTPS with the TLS listener in Host PR 12103. The same assignment can therefore advertise
`http://100.86.0.4:49123/` or `https://100.86.0.4:49123/` according to the listener.
The supplied mapped port is preserved, even when it is a scheme's default port.
Legion does not select the HTTP listener protocol.

The accepted string is immutable assignment identity. Identical retries return
204; a changed string (including null versus non-null) returns 409, using the
same ordinal comparison as the other assignment strings. It is not applied to
the worker environment or published in the management state response.

When a worker advertises `HttpUri`, Proxy retains the real worker-local destination
(or explicit `WorkerHttpEndpoint` override) for forwarding and rewrites only the
outgoing capability to the mapped origin composed using the proxy listener's scheme. A missing assigned endpoint
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
configuration retains its full HTTP(S)-origin format and remains available **only to unassigned legacy sessions** (including
the container-only local harness). It is never a fallback for an accepted assignment.
The project harness sends the host and explicit port from Aspire's allocated endpoint in its assignment.
Coordinate Legion producer and Host consumer rollout: an older consumer may ignore
the additive field, while an older producer that omits it cannot initialize an
assigned HTTP worker. `startupMode` is unchanged.

When reconciling Host PRs 12090 and 12103, retain the listener-selected scheme and pass
the same accepted assignment to capability finalization for both initialization and
environment reload. The finalizer requires an explicit assignment argument; only
deliberate unassigned legacy callers pass null. TLS listener/certificate configuration
remains with PR 12103, and the combined TLS/reload path requires its own validation.

Focused tests cover management serialization/validation, endpoint retry identity,
missing endpoint versus non-HTTP behavior, assigned capability propagation,
real local Proxy-to-Worker HTTP execution, and existing legacy/Host capability
consumption. These are not one combined cross-repo E2E, deployment, or private-stamp
validation.

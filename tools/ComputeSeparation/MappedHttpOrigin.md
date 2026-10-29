# WorkerProxy assigned HTTP origin

The platform supplies `httpProxyEndpoint` as a required **HTTP(S) origin** string on
`PUT /admin/worker/assignment`. Legion carries its allocated `28080` mapping from HNA in
`PodSpecializationDescriptor.WorkerHttpEndpoint`; NNA copies the typed `PodIpv4Endpoint?`
into `FunctionsWorkerProxyAssignmentRequest.HttpProxyEndpoint`. It serializes as
`http://<ip>:<port>` after allocation for every split-worker function group. It is not a container creation/startup setting.

Example addition to an otherwise unchanged assignment:

```json
{ "httpProxyEndpoint": "http://100.86.0.4:49123" }
```

All existing fields, including `startupMode`, retain their contracts. The endpoint
requires a concrete host and a nonzero port; HTTP(S) scheme-default ports are accepted.
IPv6 hosts must be bracketed, for example `http://[::1]:49123`.
Path prefixes, credentials, queries, fragments, wildcard hosts, unsupported or missing
schemes, and malformed values return `400 InvalidValue` targeting
`httpProxyEndpoint` without claiming assignment. Null, omitted, or blank values return
`400 Required`, including for non-HTTP function groups.

The origin preserves its supplied scheme and port. Scheme-less `host:port` values
are not accepted. TLS listener/certificate
configuration is separate; accepting an HTTPS origin does not enable a TLS listener.

The accepted string is immutable assignment identity. Identical retries return
204; a changed valid string returns 409, using the
same ordinal comparison as the other assignment strings. It is not applied to
the worker environment or published in the management state response.

When a worker advertises `HttpUri`, Proxy retains the real worker-local destination
(or explicit `WorkerHttpEndpoint` override) for forwarding and rewrites only the
outgoing capability to the mapped origin. A missing endpoint fails assignment.
The origin is supplied only through assignment, not startup configuration. No inferred
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
later assignment. Without an accepted, ready assignment, HTTP initialization fails.

Both project and container harnesses use the fake platform to assign before linking.
The project harness sends Aspire's allocated endpoint URI in its assignment;
the container harness sends the Proxy's Docker-network origin, reachable from the Host container.
Aspire configures these addresses on the fake platform, which supplies the assignment;
WorkerProxy does not read an origin from an environment variable or startup option.
Coordinate Legion producer and Host consumer rollout: an older consumer may ignore
the additive field, while an older producer that omits it cannot assign a worker.
`startupMode` is unchanged.

When reconciling Host PRs 12090 and 12103, retain the supplied scheme for absolute origins and pass
the same accepted assignment to capability finalization for both initialization and
environment reload. The finalizer requires an explicit assignment argument;
null means there is no ready assignment and cannot initialize HTTP forwarding. TLS listener/certificate configuration
remains with PR 12103, and the combined TLS/reload path requires its own validation.

Focused tests cover management serialization/validation, endpoint retry identity,
required endpoint validation and non-HTTP behavior, assigned capability propagation,
real local Proxy-to-Worker HTTP execution, rejection of obsolete startup configuration, and existing Host capability
consumption. These are not one combined cross-repo E2E, deployment, or private-stamp
validation.

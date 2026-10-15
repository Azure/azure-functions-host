# WorkerProxy HTTP startup origin

The platform sets `WORKERPROXY__HTTPPROXYENDPOINT` before the WorkerProxy
container starts. Standard configuration binds it to
`WorkerProxy:HttpProxyEndpoint`. This must be the actual allocated external
HTTP origin, not the local listener port or external gRPC port plus one.
Neither WorkerAssignRequest nor assignment state/retry identity contains
the endpoint.

When the worker advertises HttpUri, Proxy preserves the worker-reported local
destination (or explicit WorkerHttpEndpoint override) for forwarding. It
rewrites only the outgoing HttpUri to the startup-configured external origin.
Existing Host client and server-backed channels consume that capability.
Missing or malformed origins fail HTTP initialization without silently changing
transport. Origins require HTTP(S), a concrete host, nonzero port, root path,
and no credentials, query, or fragment. Same-origin destinations fail to prevent
forwarding loops. Workers without HttpUri retain non-HTTP behavior.

Explicit local/legacy configured origins remain supported. There is no inferred
default, assignment override, private bypass, fixed external port, or pod-IP
placeholder.

## Producer/consumer ordering

The companion Legion producer reserves and commits worker Swift mappings before
NNA starts containers and materializes the origin in the Proxy startup env.
Later AllocatePod reuses and validates that exact reservation. Coordinate
producer and consumer rollout; an old HNA allocates after container startup and
cannot satisfy the new startup-env contract. PR04 startupMode remains a
separate prerequisite; PR02 platform identity is orthogonal.

Tests cover production environment binding, malformed/missing origin behavior,
outgoing capabilities with local-destination retention, real local Proxy-to-
Worker HTTP execution with a preconfigured nonadjacent origin, and Host
capability consumption separately. They do not claim one combined cross-repo
E2E execution, private-stamp validation, or production readiness.

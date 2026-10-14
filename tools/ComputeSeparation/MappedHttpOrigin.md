# Allocated WorkerProxy HTTP origin

WorkerProxy's local HTTP listener port is not its externally mapped port.
The platform must send the absolute external origin in `httpProxyEndpoint`
on the existing worker assignment before the successful worker initialization
response. WorkerProxy may start without this value.

When the worker advertises `HttpUri`, WorkerProxy captures the worker-reported
local destination (or the explicit `WorkerHttpEndpoint` override) for forwarding.
It rewrites only the outgoing `HttpUri` capability to the assigned external
origin. The Host's existing client and server-backed worker channels consume
that capability. No worker-link HTTP field, port arithmetic, fixed external port,
pod-IP template, or private bypass is required.

Assignments reject malformed origins without claiming the pod. Origins must be
HTTP(S) with a concrete host, nonzero port, root path, and no credentials, query,
or fragment. Equivalent assignment retries preserve the origin; changing it
conflicts. HTTP initialization without an assigned or explicitly configured
origin fails the relay and withdraws the local destination instead of changing
transport to gRPC. Worker and proxy origins must differ to prevent a forwarding
loop. Workers without `HttpUri` retain their non-HTTP behavior.

## Local and legacy policy

An explicitly configured `WorkerProxy:HttpProxyEndpoint` remains supported
for local development and legacy deployments without dynamic assignment.
The assigned origin takes precedence; configuration never supplies an inferred
default. Do not enable the private `SkipHttpProxyEndpointAdvertisement`
workaround or derive the external HTTP port from the gRPC endpoint. Neither
workaround exists on this change's upstream `dev` baseline, and neither private
commit is imported.

## Producer/consumer rollout

The companion Legion producer reads the actual HTTP allocation mapping in HNA,
adds it to the existing assignment, and deletes an allocation that cannot supply
its required mapping. Deploy this consumer before relying on dynamic origins.
Old producers can still use the explicit configured-origin policy; old consumers
ignore the additive assignment field and therefore still require a correct
configured origin. PR04's explicit startup-mode producer is a separate prerequisite
for the complete Legion-to-Proxy assignment contract. PR02's platform pod identity
fix is orthogonal and must be preserved during integration.

The tests exercise allocation-independent origin delivery/retry validation,
real local Proxy-to-Worker HTTP execution using an assigned origin, and Host
consumption of a nonadjacent negotiated capability. These are local harness tests,
not private-stamp validation or evidence of production readiness.

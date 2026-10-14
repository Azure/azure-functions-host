# Explicit Worker startup mode rollout

Legion must send `startupMode: "Preconfigured"` for the current BYOC creation
flow: the customer worker starts with its final application configuration.
This is a caller-selected contract value, not a pool or Proxy configuration
default. Both supported values (`Preconfigured`, `SpecializationRequired`) are
case-sensitive. Recording a mode does not perform specialization.

The upstream Host baseline already requires the field. The private
`DefaultStartupMode` option and handler/validator plumbing were uncommitted
workarounds, not part of that baseline; do not carry them into an upstream PR
or a new image. The contract tests deliberately supply the obsolete configuration
key and prove it cannot make a missing or invalid wire mode succeed.

## Coordination and rollback

1. Land/build the Legion producer change against `main` and verify captured
   assignment JSON contains the explicit value.
2. Converge every producer before using a strict consumer in a private
   environment that previously relied on the workaround. Drain or recreate
   assignments staged by older producers without a mode. Check outstanding
   persisted assignment contexts as well as newly created workers.
3. Use a Host build without the private default plumbing. Keep identity,
   environment, directory validation and exact retry equality unchanged.
   Identical retries return 204; changing only a valid mode returns 409.
4. Do not roll a producer back to omission while a strict consumer is serving
   new assignments. Coordinate producer/consumer rollback or stop new BYOC
   assignments until the explicit producer is restored.

Existing explicit-mode callers need no new fields and retain their behavior.
The producer field is additive to the wire payload, but a producer that omits
it is intentionally not compatible with the strict consumer. Mixed Legion
versions reconstructing assignment contexts require convergence before
creating new explicit-mode contexts; this is not a new optional/default mode.
This change does not deploy anything, alter pool defaults, change images, or
implement a specialization engine.

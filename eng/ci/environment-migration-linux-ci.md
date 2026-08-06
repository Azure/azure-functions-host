# Environment migration Linux CI contract

Phase 1 requires focused configuration-provider contracts to execute on Linux
in the public PR pipeline.

## Phase 0 constraints

At the Phase 0 inventory freeze:

- `eng/ci/public-build.yml` used pool `1es-pool-azfunc-public` with image
  `1es-windows-2022` and ran `run-unit-tests.yml` only on Windows.
- `eng/ci/templates/jobs/run-unit-tests.yml` did not expose a pool or OS
  parameter.
- The official, non-PR build used the different internal pool
  `1es-pool-azfunc`. Its Linux artifact job selected
  `1es-ubuntu-24.04-min`.
- No checked-in pipeline selected an Ubuntu image from
  `1es-pool-azfunc-public`.

The internal-pool image was not evidence that the same image was available in
the public pool, so Phase 1 remained gated on an exact public-pool image.

## Resolution

On August 5, 2026, the Azure Portal pool configuration for
`1es-pool-azfunc-public` explicitly listed image alias
`1es-ubuntu-24.04-min` with a 19% buffer. This resolves the image-availability
gate.

Repository configuration now provides:

- `eng/ci/public-build.yml` uses pool `1es-pool-azfunc-public` with image
  `1es-windows-2022` for the existing Windows unit-test job.
- `eng/ci/templates/jobs/run-environment-provider-contract-tests.yml` adds a
  separate job on `1es-pool-azfunc-public`, image
  `1es-ubuntu-24.04-min`, and `os: linux`.
- The Linux job restores and builds only `WebJobs.Script.Tests` and its
  subprocess test host, then runs the focused
  `ScriptEnvironmentVariablesConfigurationSourceTests` filter.
- The subprocess test host is also listed in the existing unit-test job's
  explicit `test_projects` list, so the Windows job builds the same isolation
  boundary before running the full unit suite.

The public pipeline uses the existing 1ES unofficial template and repository
authentication steps. The focused job introduces no internal pool, protected
resource, or secret-only condition, preserving compatibility with public and
fork pull-request validation. The first public pipeline execution remains the
validation of the complete 1ES job and fork-PR path.

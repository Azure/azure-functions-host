# Environment migration Linux CI feasibility gate

Phase 1 requires focused configuration-provider contracts to execute on Linux
in the public PR pipeline. Repository configuration currently proves:

- `eng/ci/public-build.yml` uses pool `1es-pool-azfunc-public` with image
  `1es-windows-2022` and runs `run-unit-tests.yml` only on Windows.
- `eng/ci/templates/jobs/run-unit-tests.yml` does not expose a pool or OS
  parameter.
- The official, non-PR build uses the different internal pool
  `1es-pool-azfunc`. Its Linux artifact job selects
  `1es-ubuntu-24.04-min`.
- No checked-in pipeline selects an Ubuntu image from
  `1es-pool-azfunc-public`.

The internal-pool image is not evidence that the same image is available in the
public pool. The Phase 1 Linux leg remains externally gated until the public
pool owner confirms an exact Ubuntu image name and fork-PR availability.

After that confirmation, Phase 1 may either parameterize
`run-unit-tests.yml` or add a focused job to `public-build.yml`. The job must
use `1es-pool-azfunc-public`, the confirmed image, `os: linux`, and run the
provider-contract filter from `WebJobs.Script.Tests`. Any subprocess harness
project must also be added to the pipeline's explicit `test_projects` list.
Phase 0 intentionally adds neither that job nor the Phase 1 provider contracts.

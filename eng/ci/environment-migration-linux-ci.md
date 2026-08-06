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
fork pull-request validation.

Public build [296279 (`4.1054.100-ci.26406.2`)](https://dev.azure.com/azfunc/public/_build/results?buildId=296279)
completed successfully for source commit
`f2028a7483050e7311d8582bc438ef47605ded69`. Its timeline confirms that both
`Run Unit Tests` and `Run Environment Provider Contract Tests (Linux)`
completed successfully on `1es-pool-azfunc-public`; the Linux job executed and
was not skipped.

## Phase 1 PR-02 specialization coverage

The assignment, EasyAuth, specialization-order, root/child options, and
root/child DI contracts live in `WebJobs.Script.Tests`, so the existing
`Run Unit Tests` public/fork PR job executes them. Process mutation before
configuration reload and child-process inheritance are part of
`ScriptEnvironmentVariablesConfigurationSourceTests`, so the focused public
Linux job also executes those subprocess-isolated contracts. No internal pool
or manual-only integration definition is required for PR-02 coverage.

Public build [296310 (`4.1054.100-ci.26406.3`)](https://dev.azure.com/azfunc/public/_build/results?buildId=296310&view=results)
completed successfully for source branch
`refs/heads/fabiocav/ienvironment-phase-1b` and source commit
`9b6f563b5681f7cbaa6838a5f806f499b499f2c3`. Its timeline confirms that both
`Run Unit Tests` and `Run Environment Provider Contract Tests (Linux)`
completed successfully; both the Windows unit-test job and the Linux
provider-contract job executed and were not skipped.

## DG-6 process-mutator shape

PR-02 approves the following shape for the later production seam; it does not
introduce that production service or migrate current writers:

```csharp
internal interface IProcessEnvironmentMutator
{
    void Set(string name, string value);
}
```

The seam is synchronous and write-only. A null value deletes the process
variable, an empty value remains present, and a completed call is immediately
visible to live process readers and processes started afterward. Assignment
continues to issue one call per payload, CORS, EasyAuth, site-update, and
platform-specific write in the current order; there is no transactional
`SetMany` operation. Live reads needed for EasyAuth precedence remain a
separate input, and configuration indexer writes are not a substitute.

The test-only `IProcessEnvironmentMutatorContract` and recording adapter encode
this shape against current assignment writers. They intentionally do not add a
production implementation before the PR-19 writer migration.

The contracts also retain the legacy `TestEnvironment` distinction: assigning
null deletes an existing key but leaves a null-valued entry when the key was
previously absent. Real-process null deletion remains covered separately by the
subprocess provider contracts.

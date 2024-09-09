### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Added fallback behavior to ensure in-proc payload compatibility with "dotnet-isolated" as the `FUNCTIONS_WORKER_RUNTIME` value (#10439)
- Update Java Worker Version to [2.17.0](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.17.0)
  - Update application insights agent version to 3.5.4
  - Includes fixes from 2.16.0
- Migrated Scale Metrics to use `Azure.Data.Tables` SDK (#10276)
  - Added support for Identity-based connections
- Skip validation of `FUNCTIONS_WORKER_RUNTIME` with funciton metadata in placeholder mode. (#10459)
### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.33.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.33.0)
- Added fallback behavior to ensure in-proc payload compatibility with "dotnet-isolated" as the `FUNCTIONS_WORKER_RUNTIME` value (#10439)
- Update Java Worker Version to [2.17.0](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.17.0)
  - Update application insights agent version to 3.5.4
  - Includes fixes from 2.16.0
- Migrated Scale Metrics to use `Azure.Data.Tables` SDK (#10276)
  - Added support for Identity-based connections
- Skip validation of `FUNCTIONS_WORKER_RUNTIME` with function metadata in placeholder mode. (#10459)
- Sanitize exception logs (#10443)
- Improving console log handling during specialization (#10345)
- Update Node.js Worker Version to [3.10.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.10.1)
- Pass ARM ID header for Linux consumption metrics requests (#10476)

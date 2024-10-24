### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.34.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.34.0)
- Added fallback behavior to ensure in-proc payload compatibility with "dotnet-isolated" as the `FUNCTIONS_WORKER_RUNTIME` value (#10439)
- Update Node.js Worker Version to [3.10.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.10.1)
- Implement host configuration property to allow configuration of the metadata provider timeout period (#10526)
  - The value can be set via `metadataProviderTimeout` in host.json and defaults to "00:00:30" (30 seconds).
  - For logic apps, unless configured via the host.json, the timeout is disabled by default.
- Added support for identity-based connections to Diagnostic Events (#10438)
- Update PowerShell 7.2 worker to [4.0.4025](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4025)
- Update PowerShell 7.4 worker to [4.0.4026](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4026)

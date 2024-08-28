### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.17.0](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.17.0)
  - Update application insights agent version to 3.5.4
  - Includes fixes from 2.16.0
- Update Python Worker Version to [4.31.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.31.0)
- Upgraded the following package versions (#10325):
  - `Azure.Security.KeyVault.Secrets` updated to 4.6.0
  - `System.Format.Asn1` updated to 6.0.1
- Update Python Worker Version to [4.30.3](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.30.3)
- Update PowerShell 7.2 worker to [4.0.4020](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4020)
- Update PowerShell 7.4 worker to [4.0.4021](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4021)
- Updated dotnet-isolated worker to [1.0.11](https://github.com/Azure/azure-functions-dotnet-worker/pull/2653) (#10379)
- Resolved thread safety issue in the `GrpcWorkerChannel.LoadResponse` method. (#10352)
- Worker termination path updated with sanitized logging (#10367)
- Avoid redundant DiagnosticEvents error message (#10395)
- Added logic to shim older versions of the .NET Worker JsonFunctionProvider to ensure backwards compatibility (#10410)
- Migrated Scale Metrics to use `Azure.Data.Tables` SDK (#10276)
  - Added support for Identity-based connections
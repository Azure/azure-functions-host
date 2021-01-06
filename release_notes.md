### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Fixed [bug](https://github.com/Azure/azure-functions-durable-extension/issues/1467) in sync triggers operations for Durable Functions using custom storage account connection strings.
- Updated PowerShell Worker to 3.0.557 (PS6) [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v3.0.557) and 3.0.560 (PS7) [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v3.0.560)
- Updated Microsoft.Azure.WebJobs and Microsoft.Azure.WebJobs.Logging.ApplicationInsights to v3.0.25 [Release Notes](https://github.com/Azure/azure-webjobs-sdk/releases/tag/v3.0.25)
- Updated Microsoft.Azure.WebJobs.Extensions.Http to 3.0.9-10815
- Updated Python Worker to 1.1.8 [Release Note](https://github.com/Azure/azure-functions-python-worker/releases/tag/1.1.8)
- Updated Python Library to 1.5.0 [Release Note](https://github.com/Azure/azure-functions-python-library/releases/tag/1.5.0)
- Updated [System.Data.SqlClient to 4.8.2](https://www.nuget.org/packages/System.Data.SqlClient/4.8.2)
- Added direct refereces to [System.IO.Pipes](https://www.nuget.org/packages/System.IO.Pipes/4.3.0)  and [System.Threading.Overlapped](https://www.nuget.org/packages/System.Threading.Overlapped/4.3.0) to ensure System.Data.SqlClient package update does not impact unification 

**Release sprint:** Sprint 87
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+87%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+87%22+label%3Afeature+is%3Aclosed) ]
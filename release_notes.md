### Release notes
<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Updated HTTP extension to [3.0.10](https://github.com/Azure/azure-webjobs-sdk-extensions/releases/tag/http-v3.0.10)
- Updated Python Worker Version to [1.1.10](https://github.com/Azure/azure-functions-python-worker/releases/tag/1.1.10)
- Configure host.json to use workflow when creating a default host.json and app is identified as a logic app. (#6810)
- Updated Node.js Worker Version to [2.1.0](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v2.1.0)
- Updated Java Worker Version to [1.8.1](https://github.com/Azure/azure-functions-java-worker/releases/tag/1.8.1)
- Updated PowerShell Worker to [3.0.629](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v3.0.629) (PS7) and [3.0.630](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v3.0.630) (PS6)
- Updated [System.Data.SqlClient to 4.8.2](https://www.nuget.org/packages/System.Data.SqlClient/4.8.2)
- Added direct refereces to [System.IO.Pipes](https://www.nuget.org/packages/System.IO.Pipes/4.3.0)  and [System.Threading.Overlapped](https://www.nuget.org/packages/System.Threading.Overlapped/4.3.0) to ensure System.Data.SqlClient package update does not impact unification 

**Release sprint:** Sprint 89
[ [bugs](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+89%22+label%3Abug+is%3Aclosed) | [features](https://github.com/Azure/azure-functions-host/issues?q=is%3Aissue+milestone%3A%22Functions+Sprint+89%22+label%3Afeature+is%3Aclosed) ]
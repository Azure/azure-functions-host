### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.20.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.20.0)
- Increased maximum HTTP request content size to 210000000 Bytes (~200MB)
- Update Node.js Worker Version to [3.8.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.8.1)
- Update PowerShell 7.4 Worker Version to [4.0.2975](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2975)
- Update PowerShell 7.2 Worker Version to [4.0.2974](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2974)
- Update PowerShell 7.0 Worker Version to [4.0.2973](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2973)
- Limit dotnet-isolated specialization to 64 bit host process (#9548)
- Sending command line arguments to language workers with `functions-` prefix to prevent conflicts (#9514)
- Update WebJobsScriptHostService to remove hardcoded sleep during application shut down (#9520)

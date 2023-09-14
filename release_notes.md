### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.12.2](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.12.2)
- Update Python Worker Version to [4.17.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.17.0)
- Increased maximum HTTP request content size to 210000000 Bytes (~200MB)
- Update Node.js Worker Version to [3.8.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.8.1)
- Update WebJobsScriptHostService to remove hardcoded sleep during application shut down (#9520)
- Update PowerShell 7.4 Worker Version to [4.0.2975](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2975)
- Update PowerShell 7.2 Worker Version to [4.0.2974](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2974)
- Update PowerShell 7.0 Worker Version to [4.0.2973](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2973)
- Bug fix: Do not restart worker channels or JobHost when an API request is made to get or update the function metadata (unless the config was changed)
  - This fixes a bug where requests to 'admin/functions' lead to a "Did not find initialized workers" error when
    worker indexing is enabled.
- Bug fix: If there are no channels created and the host is running, restart the JobHost instead of shutting down worker channels
  - This fixes a bug with worker indexing where we are shutting down worker channels and creating a new channel that never
    gets properly initialized as the invocation buffers are not created - this leads to a "Did not find initialized workers" error.

### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Add support for standalone executable (ie: `dotnet build --standalone`) for out-of-proc workers in Linux Consumption. (#9550)
- Bug fix: Do not restart worker channels or JobHost when an API request is made to get or update the function metadata (unless the config was changed) (#9510)
  - This fixes a bug where requests to 'admin/functions' lead to a "Did not find initialized workers" error when
    worker indexing is enabled.
- Bug fix: If there are no channels created and the host is running, restart the JobHost instead of shutting down worker channels (#9510)
  - This fixes a bug with worker indexing where we are shutting down worker channels and creating a new channel that never
    gets properly initialized as the invocation buffers are not created - this leads to a "Did not find initialized workers" error.
- Check if a blob container or table exists before trying to create it (#9555)
- Adding code to simulate placeholder and specilization locally (#9618)
- Enable worker indexing by default without the need for hosting config (#9574)
- Delaying execution of `LogWorkerMetadata` method until after coldstart is done. (#9627)
- Update PowerShell Worker 7.2 to 4.0.3070 [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.3070)
- Update PowerShell Worker 7.4 to 4.0.3030 [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.3030)
- Update Node.js Worker Version to [3.9.0](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.9.0)
- Upgrading dependent packages to latest versions. #9646
   - Azure.Identity (1.10.0 to 1.10.3)
   - Azure.Core (1.34.0 to 1.35.0)
- Updating HostWarmupMiddleware to trigger warmup code on home page request when testing locally using DebugPlaceholder or ReleasePlaceholder configuration.
- Bug fix: Comparisons in the Azure Key Vault Secrets Repository are now case insensitive (#9644)
  - This fixes a bug where keys could be automatically recreated if they had been manually added to Key Vault with all lowercase secret names
- Update DotNetIsolatedNativeHost version to 1.0.3 (#9671)
- Update PowerShell Worker 7.4 to 4.0.3085 [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.3085)
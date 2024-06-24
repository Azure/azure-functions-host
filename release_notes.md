### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Fixed a bug where non HTTP invocation responses were processed by `IHttpProxyService` when HTTP proxying capability is enabled (#9984)
- Update Python Worker Version to [4.29.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.28.1)
- Fixed an issue causing sporadic HTTP request failures when worker listeners were not fully initialized on first request #9954
- Update Node.js Worker Version to [3.10.0](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.10.0) (#9999)
- Update PowerShell worker 7.2 to [4.0.3220](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.3220)
- Update PowerShell worker 7.4 to [4.0.3219](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.3219)
- Update Azure.Identity to 1.11.0 (#10002)
- Fixed an issue leading to a race when invocation responses returned prior to HTTP requests being sent in proxied scenarios.
- Language worker channels will not be started during placeholder mode if we are in-process (#10161)
- Ordered invocations are now the default (#10201)
- Fixed incorrect function count in the log message.(#10220)
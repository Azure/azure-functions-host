### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.18.0](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.18.0)

- Updated Microsoft.Extensions.DependencyModel to 6.0.2 and 8.0.2 for .NET 6 and .NET 8, respectively. (#10661)
- Update the `DefaultHttpProxyService` to better handle client disconnect scenarios (#10688)
  - Replaced `InvalidOperationException` with `HttpForwardingException` when there is a ForwarderError
- [In-proc] Codeql : Fix to remove exception details from the response (#10751)
- Update PowerShell 7.4 worker to [4.0.4134](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4134)
- Update Python Worker Version to [4.35.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.35.0)
- Update domain for CDN URI
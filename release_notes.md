### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Updated Microsoft.Extensions.DependencyModel to 6.0.2 and 8.0.2 for .NET 6 and .NET 8, respectively. (#10661)
- Update the `DefaultHttpProxyService` to better handle client disconnect scenarios (#10688)
  - Replaced `InvalidOperationException` with `HttpForwardingException` when there is a ForwarderError
- [In-proc] Codeql : Fix to remove exception details from the response (#10751)

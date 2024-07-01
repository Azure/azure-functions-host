### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Fixed a bug where non HTTP invocation responses were processed by `IHttpProxyService` when HTTP proxying capability is enabled (#9984)
- Update Python Worker Version to [4.29.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.28.1)
- Language worker channels will not be started during placeholder mode if we are in-process (#10161)
- Ordered invocations are now the default (#10201)
- Skip worker description if none of the profile conditions are met (#9932)
- Fixed incorrect function count in the log message.(#10220)

### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Marking SyncTriggers [RequiresRunningHost]. (#10233)
- Defaulting SwtAuthenticationEnabled to False (#10195)
- Adding runtime site name to valid JWT audiences (slot scenarios) (#10185)
- Update Python Worker Version to [4.29.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.28.1)
- Language worker channels will not be started during placeholder mode if we are in-process (#10161)
- Ordered invocations are now the default (#10201)
- Add IsDisabled property to worker description and skip if the value (#10250)
- Fixed incorrect function count in the log message.(#10220)
- Migrate Diagnostic Events to Azure.Data.Tables (#10218)
- Sanitize worker arguments before logging (#10260)

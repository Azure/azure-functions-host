### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Rev `Microsoft.Azure.Functions.DotNetIsolatedNativeHost` to `1.0.13` (#11269)
- Refactor telemetry & exporter setup: deprecations, noise reduction, and API updates (#11260)
- Update Python Worker Version to [4.39.2](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.39.2)
- Add JitTrace Files for v4.1043 (#11281)
- Update `Microsoft.Azure.WebJobs` reference to `3.0.42` (#11309)
- Adding Activity wrapper to create a function-level request telemetry when none exists (#11311)
- Setting current activity status for failed invocations (#11313)
- Adding test coverage for `Utility.IsAzureMonitorLoggingEnabled` (#11322)
- Reduce allocations in `Utility.IsAzureMonitorLoggingEnabled` (#11323)
- Update PowerShell worker to [4.0.4581](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.4581)
- Bug fix that fails in-flight invocations when a worker channel shuts down (#11159)

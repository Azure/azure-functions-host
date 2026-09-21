### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Update PS 7.4 worker to [v4.0.5303](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5303) (#11905)
- Update PS 7.6 worker to [v4.0.5302](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5302) (#11905)
- Ensure the gRPC server is available when an app transitions online after starting with app_offline.htm.
- Add usage telemetry for worker custom metrics and Azure Monitor diagnostic logging (#12034)
- Fixed Linux language worker SIGTERM exits being reported as worker failures. (#11944)
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task). (#11936)
- Ensure hosted services are stopped when application shutdown cancels startup, preventing IIS/ANCM from remaining stuck serving HTTP 500.30 (#11953)
- Preserve W3C trace context for session-enabled Service Bus triggers when OpenTelemetry is enabled. (#11946)

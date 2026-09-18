### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Fixed Linux language worker SIGTERM exits being reported as worker failures. (#11944)
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task). (#11936)
- Ensure hosted services are stopped when application shutdown cancels startup, preventing IIS/ANCM from remaining stuck serving HTTP 500.30 (#11953)
- Preserve W3C trace context for session-enabled Service Bus triggers when OpenTelemetry is enabled. (#11946)
- Stopped populating the `test_data` and `test_data_href` properties on the functions metadata API response and stopped persisting test data on function create/update. The properties remain in the payload as `null`, and existing test data files are still accessible via `/admin/vfs`. (#TBD)

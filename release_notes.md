### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Fixed Linux language worker SIGTERM exits being reported as worker failures. (#11944)
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task). (#11936)
- Update PS 7.4 worker to [v4.0.5361](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5361) (#11931)
- Update PS 7.6 worker to [v4.0.5362](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5362) (#11931)
- Ensure the gRPC server is available when an app transitions online after starting with app_offline.htm.
- Extract host-managed worker and Grpc Server behavior into Azure.Functions.Rpc.Server.csproj (#11916)
- Use a structured composite key for the authorization cache to prevent cache key collisions.
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task).
- Ensure hosted services are stopped when application shutdown cancels startup, preventing IIS/ANCM from remaining stuck serving HTTP 500.30 (#11953)

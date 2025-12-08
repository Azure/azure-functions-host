### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Improve resiliency of batch operations in TableStorageScaleMetricsRepository with Polly retry and exponential backoff (#11586)
- Add support for propagating tags from the worker to the host and update the protobuf version to `v1.12.0-protofile` (#11575)
- Restart worker process if not disposed (shutting down) and exited with code 0 (#11576)
- Fix race condition in SecretManager secret caching with double-check locking pattern (#11560)
- Updating OTel related packages (#11568)
- Fixed worker configuration cache invalidation to properly refresh language worker options during host restarts with extension bundles (#11582)
- Logging environment value of LocalSitePackagesPath in RunFromPackageHandler (#11541)
- Improve timer trigger schedule validation for all consumption plans: accept 5 and 6-digit CRON expressions, apply validation to all consumption SKUs, and warn on non-CRON schedules for Linux Consumption (#11601)
- Adding a "web app" configuration profile (#11447)
- Add JitTrace Files for v4.1045
- Throw exception instead of timing out when worker channel exits before initializing gRPC (#10937)
- Adding empty remote message check in the SystemLogger (#11473)
- Fix `webPubSubTrigger`'s for Flex consumption sku (#11489)
- Suppress execution context flow into script host start (#11498)
- Add AzureWebJobsStorage health check (#11471)
- Refactor functions worker runtime retrieval to use `IWorkerRuntimeResolver` abstraction (11511)

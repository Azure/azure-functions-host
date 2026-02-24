### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Refactor DiagnosticEventTableStorageRepository TableClient (#11611)
- Improve resiliency of batch operations in TableStorageScaleMetricsRepository with Polly retry and exponential backoff (#11586)
- Add support for propagating tags from the worker to the host and update the protobuf version to `v1.12.0-protofile` (#11575)
- Restart worker process if not disposed (shutting down) and exited with code 0 (#11576)
- Fix race condition in SecretManager secret caching with double-check locking pattern (#11560)
- Updating OTel related packages (#11568)
- Fixed worker configuration cache invalidation to properly refresh language worker options during host restarts with extension bundles (#11582)
- Logging environment value of LocalSitePackagesPath in RunFromPackageHandler (#11541)
- Enable options logging for WebHost-level options by calling `AddFormattableOptionsLogging` and update `Microsoft.Azure.WebJobs` packages to `3.0.45` (#11615)
- Improve timer trigger schedule validation for all consumption plans: accept 5 and 6-digit CRON expressions, apply validation to all consumption SKUs, and warn on non-CRON schedules for Linux Consumption (#11601)
- Update NodeJS Worker Version to [3.13.0](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.13.0)

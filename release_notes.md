### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Add support for propagating tags from the worker to the host and update the protobuf version to `v1.12.0-protofile` (#11575)
- Restart worker process if not disposed (shutting down) and exited with code 0 (#11576)
- Fix race condition in SecretManager secret caching with double-check locking pattern (#11560)
- Fixed worker configuration cache invalidation to properly refresh language worker options during host restarts with extension bundles (#11582)
- Logging environment value of LocalSitePackagesPath in RunFromPackageHandler (#11541)
- Improve timer trigger schedule validation for all consumption plans: accept 5 and 6-digit CRON expressions, apply validation to all consumption SKUs, and warn on non-CRON schedules for Linux Consumption (#11601)
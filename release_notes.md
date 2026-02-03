### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Improve resiliency of batch operations in TableStorageScaleMetricsRepository with Polly retry and exponential backoff (#11586)
- Restart worker process if not disposed (shutting down) and exited with code 0 (#11576)
- Fix race condition in SecretManager secret caching with double-check locking pattern (#11560)

### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Fix race condition in SecretManager secret caching with double-check locking pattern (#11560)
- Fixed worker configuration cache invalidation to properly refresh language worker options during host restarts with extension bundles (#11582)
### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Implement host configuration property to allow configuration of the metadata provider timeout period (#10526)
  - The value can be set via `metadataProviderTimeout` in host.json and defaults to "00:00:30" (30 seconds).
  - For logic apps, unless configured via the host.json, the timeout is disabled by default.

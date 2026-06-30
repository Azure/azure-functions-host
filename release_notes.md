### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Fixed a WebHost worker channel shutdown race that could hang host teardown when a language worker was still initializing. (#11853)

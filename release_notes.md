### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.45.1](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.45.1) (#11860)
- Fixed extension system keys being overwritten on host restart when the startup context supplies an incomplete secrets snapshot, which broke Event Grid and other webhook deliveries with 401s (#11904)
-->
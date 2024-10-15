### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.33.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.33.0)
- Sanitize exception logs (#10443)
- Improving console log handling during specialization (#10345)
- Update Node.js Worker Version to [3.10.1](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.10.1)
- Remove packages `Microsoft.Azure.Cosmos.Table` and `Microsoft.Azure.DocumentDB.Core` (#10503)
- Implement host configuration property to all configuration of the metadata provider timeout (#10526)
  - The value can be set via `metadataProviderTimeout` in host.json and defaults to "00:00:30" (30 seconds)

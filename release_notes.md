### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Update PS 7.4 worker to [v4.0.5303](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5303) (#11905)
- Update PS 7.6 worker to [v4.0.5302](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5302) (#11905)
- Ensure the gRPC server is available when an app transitions online after starting with app_offline.htm.
- Extract host-managed worker and Grpc Server behavior into Azure.Functions.Rpc.Server.csproj (#11916)
- Use a structured composite key for the authorization cache to prevent cache key collisions.
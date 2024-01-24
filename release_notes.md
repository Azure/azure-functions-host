### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Python Worker Version to [4.24.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.24.0)
- Updated `Microsoft.Azure.Functions.DotNetIsolatedNativeHost` version to 1.0.5 (#9753)
- Add function grouping information (https://github.com/Azure/azure-functions-host/pull/9735)
- Bump `Microsoft.IdentityModel.Tokens`, `Microsoft.IdentityModel.Protocols.OpenIdConnect`, and
  `System.IdentityModel.Tokens.Jwt` from 6.32.0 to 6.35.0 (#9793)
- Implement host configuration property for handling pre-cancelled invocation requests (#9523)
  - If a worker supports CancellationTokens, cancelled invocations will now be sent to the worker by default
    - Customers can opt-out of this behavior by setting `SendCanceledInvocationsToWorker` to `false` in host.json
  - If a worker does not support CancellationTokens, cancelled invocations will not be sent to the worker
- Warn when `FUNCTIONS_WORKER_RUNTIME` is not set (#9799)

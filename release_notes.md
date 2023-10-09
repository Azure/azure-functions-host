### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Updated `Microsoft.Azure.Functions.DotNetIsolatedNativeHost` version to 1.0.5 (#9753)
- Implement host configuration property for handling pre-cancelled invocation requests (#9523)
  - If a worker supports CancellationTokens, cancelled invocations will now be sent to the worker by default
    - Customers can opt-out of this behavior by setting `SendCanceledInvocationsToTheWorker` to `false` in host.json
  - If a worker does not support CancellationTokens, cancelled invocations will not be sent to the worker

### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Authenticate compute-separated Link and WorkerProxy management, preserve requested assignment metadata and safe pre-customer-input retries including optional identity bootstrap, negotiate mapped HTTPS origins, report safe auth diagnostics, isolate WorkerProxy HTTP transport, and validate directional proofs with startup-provisioned TLS. (#12103)
- Update PS 7.4 worker to [v4.0.5575](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5575) (#12110)
- Update PS 7.6 worker to [v4.0.5576](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.5576) (#12110)
- `Executing HTTP request` no longer logs the user agent or request URI, `Executed HTTP request` now logs matched function and MVC route templates, `Sanitizer` redacts email addresses, host status startup errors are sanitized, and trace context attribute values are no longer logged. (#12039)
- Fix false AzureWebJobsStorage health failures for identities with documented blob data permissions while retaining low-cost connectivity probes (#12086)
- Require exact canonical master key secret names in the Key Vault secrets repository (#12083)
- Add usage telemetry for worker custom metrics and Azure Monitor diagnostic logging (#12034)
- Suppress Storage queue and blob listener polling noise from telemetry by event name, preserving listener lifecycle and diagnostic logs (#11947)
- Update Python Worker Version to [4.46.2](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.46.2) (#12082)
- Emit successful function execution counts in Flex Consumption metrics records (#12068)

### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- `Executing HTTP request` no longer logs the user agent or request URI, `Executed HTTP request` now logs JSON-escaped matched function and MVC route templates instead, malformed Unicode and non-fatal request tracing failures do not interrupt requests, `Sanitizer` redacts email addresses, and trace context attribute values are no longer logged. (#12039)
- Add usage telemetry for worker custom metrics and Azure Monitor diagnostic logging (#12034)
- Suppress Storage queue and blob listener polling noise from telemetry by event name, preserving listener lifecycle and diagnostic logs (#11947)
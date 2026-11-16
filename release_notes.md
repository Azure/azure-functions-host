### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Let `deployment.environment.name` in `OTEL_RESOURCE_ATTRIBUTES` take priority over `WEBSITE_SLOT_NAME` in the OpenTelemetry resource, to allow overriding the environment within App Service (#12193)
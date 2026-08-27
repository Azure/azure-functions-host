### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Report AzureWebJobsStorage health only for active script hosts with storage configured; placeholder hosts no longer expose the check. (#11927)
- Reduce WebJobs storage health-check overhead by reading blob service properties instead of listing containers. (#11925)
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task). (#11936)
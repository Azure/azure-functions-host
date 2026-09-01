### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->

- Fixed worker startup errors during metadata indexing immediately exhausting the language worker restart budget instead of retrying. (#11955)
- Fixed Linux language worker SIGTERM exits being reported as worker failures. (#11944)
- Prevent extension system keys from being regenerated and overwritten when the startup context cache is stale, which previously could invalidate already-published extension webhook URLs (e.g. Event Grid, Durable Task). (#11936)

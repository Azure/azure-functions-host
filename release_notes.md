### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Introduced proper handling in environments where .NET in-proc is not supported.
- Suppress `JwtBearerHandler` logs from customer logs (#10617)
- Updated System.Memory.Data reference to 8.0.1
- Address issue with HTTP proxying throwing `ArgumentException` (#10616)
- Updated JobHost restart suppresion in functions APIs to align with request lifecycle (#10638)

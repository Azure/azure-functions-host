### Release notes

<!-- Please add your release notes in the following format:
- My change description (#PR)
-->
- Update Java Worker Version to [2.12.1](https://github.com/Azure/azure-functions-java-worker/releases/tag/2.12.1)
- Update Python Worker Version to [4.15.0](https://github.com/Azure/azure-functions-python-worker/releases/tag/4.15.0)

- Remove feature flag for http proxying (https://github.com/Azure/azure-functions-host/pull/9341)
- Add error handling for http proxying failure scenarios (https://github.com/Azure/azure-functions-host/pull/9342)
- Update PowerShell Worker 7.0 to 4.0.2850 [Release Note](https://github.com/Azure/azure-functions-powershell-worker/releases/tag/v4.0.2850)
- Update Node.js Worker Version to [3.8.0](https://github.com/Azure/azure-functions-nodejs-worker/releases/tag/v3.8.0)
- Identity dependencies updated to 6.31.0:
  - Microsoft.IdentityModel.Tokens
  - System.IdentityModel.Tokens.Jwt
  - Microsoft.IdentityModel.Abstractions
  - Microsoft.IdentityModel.JsonWebTokens
  - Microsoft.IdentityModel.Logging
- Updated Grpc.AspNetCore package to 2.55.0 (https://github.com/Azure/azure-functions-host/pull/9373)
- Update protobuf file to v1.10.0 (https://github.com/Azure/azure-functions-host/pull/9405)
- Send an empty RpcHttp payload if proxying the http request to the worker (https://github.com/Azure/azure-functions-host/pull/9415)
- Add new Host to Worker RPC extensibility feature for out-of-proc workers. (https://github.com/Azure/azure-functions-host/pull/9292)
- Apply capabilities on environment reload (placeholder mode scenarios) (https://github.com/Azure/azure-functions-host/pull/9367)

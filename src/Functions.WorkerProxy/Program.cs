// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy;
using Microsoft.AspNetCore.Builder;

WebApplication app = WorkerProxyApplication.Build(args);
await app.RunAsync();

// Expose the generated top-level entry point to the friend test assembly for WebApplicationFactory.
internal partial class Program;

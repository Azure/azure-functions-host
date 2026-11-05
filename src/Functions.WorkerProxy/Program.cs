// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Builder;

WorkerProxyStartupTime startupTime = WorkerProxyStartupTime.Capture(TimeProvider.System);
WebApplication app = WorkerProxyApplication.Build(args, startupTime);
await app.RunAsync();

// Expose the generated top-level entry point to the friend test assembly for WebApplicationFactory.
internal partial class Program;

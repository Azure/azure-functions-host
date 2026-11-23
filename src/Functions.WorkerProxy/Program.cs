// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy;

await WorkerProxyApplication.RunAsync(args);

// Expose the generated top-level entry point to the friend test assembly for WebApplicationFactory.
internal partial class Program;

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

// Direct worker launch has no Core Tools process to supply the directory containing this fixture's user assemblies.
Environment.SetEnvironmentVariable("FUNCTIONS_APPLICATION_DIRECTORY", AppContext.BaseDirectory);

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

if (builder.Configuration.GetValue("ComputeSeparation:AcquireIdentityBeforeStart", false))
{
    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
    _ = await new ManagedIdentityCredential().GetTokenAsync(
        new TokenRequestContext(["https://vault.azure.net/.default"]), timeout.Token);
    Console.WriteLine("Managed identity token acquired through WorkerProxy before worker startup.");
}

await builder.Build().RunAsync();

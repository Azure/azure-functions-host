// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Hosting;

// Direct worker launch has no Core Tools process to supply the directory containing this fixture's user assemblies.
Environment.SetEnvironmentVariable("FUNCTIONS_APPLICATION_DIRECTORY", AppContext.BaseDirectory);

FunctionsApplicationBuilder builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

await builder.Build().RunAsync();

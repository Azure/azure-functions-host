// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using TestExtensions;

[assembly: WebJobsStartup(typeof(AssemblyLoadContextRace.Startup))]

namespace AssemblyLoadContextRace;

/// <summary>Registers the extension-owned assembly-unification regression endpoint.</summary>
public sealed class Startup : IWebJobsStartup
{
    /// <inheritdoc />
    public void Configure(IWebJobsBuilder builder)
    {
        builder.Services.AddSingleton<IFunctionProvider>(new TestExtensionFunctionProvider(
            TestExtensionFunctionProvider.Http(typeof(Function1), nameof(Function1), nameof(Function1.Run), "get")));
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using TestExtensions;

[assembly: WebJobsStartup(typeof(ReferenceOlderRuntimeAssembly.Startup))]

namespace ReferenceOlderRuntimeAssembly;

/// <summary>Registers the extension-owned host-assembly compatibility regression endpoint.</summary>
public sealed class Startup : IWebJobsStartup
{
    /// <inheritdoc />
    public void Configure(IWebJobsBuilder builder)
    {
        builder.Services.AddSingleton<IFunctionProvider>(new TestExtensionFunctionProvider(
            TestExtensionFunctionProvider.Http(typeof(ReferenceOlderRuntimeAssembly),
                nameof(ReferenceOlderRuntimeAssembly), nameof(ReferenceOlderRuntimeAssembly.Run), "get")));
    }
}

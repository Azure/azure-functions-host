// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using TestExtensions;

[assembly: WebJobsStartup(typeof(NativeDependencyWithTargetFramework.Startup))]

namespace NativeDependencyWithTargetFramework;

/// <summary>Registers the extension-owned native-dependency regression endpoint.</summary>
public sealed class Startup : IWebJobsStartup
{
    /// <inheritdoc />
    public void Configure(IWebJobsBuilder builder)
    {
        builder.Services.AddSingleton<IFunctionProvider>(new TestExtensionFunctionProvider(
            TestExtensionFunctionProvider.Http(typeof(NativeDependencyNoRuntimes),
                nameof(NativeDependencyNoRuntimes), nameof(NativeDependencyNoRuntimes.Run), "get")));
    }
}

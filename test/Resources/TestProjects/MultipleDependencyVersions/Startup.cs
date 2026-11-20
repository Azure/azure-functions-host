// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.DependencyInjection;
using TestExtensions;

[assembly: WebJobsStartup(typeof(MultipleDependencyVersions.Startup))]

namespace MultipleDependencyVersions;

/// <summary>Registers the extension-owned dependency-version regression endpoint.</summary>
public sealed class Startup : IWebJobsStartup
{
    /// <inheritdoc />
    public void Configure(IWebJobsBuilder builder)
    {
        builder.Services.AddSingleton<IFunctionProvider>(new TestExtensionFunctionProvider(
            TestExtensionFunctionProvider.Http(typeof(MultipleDependencyVersions),
                nameof(MultipleDependencyVersions), nameof(MultipleDependencyVersions.Run), "get")));
    }
}

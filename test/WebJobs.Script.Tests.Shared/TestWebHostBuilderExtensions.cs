// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Hosting
{
    public static class TestWebHostBuilderExtensions
    {
        public static IWebHostBuilder AddScriptHostBuilder(this IWebHostBuilder webHostBuilder, Action<IWebJobsBuilder> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IConfigureBuilder<IWebJobsBuilder>>(_ => new DelegatedConfigureBuilder<IWebJobsBuilder>(builder)));
    }
}

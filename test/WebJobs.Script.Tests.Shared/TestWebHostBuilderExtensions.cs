// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.AspNetCore.Hosting
{
    public static class TestWebHostBuilderExtensions
    {
        public static IWebHostBuilder AddScriptHostBuilder(this IWebHostBuilder webHostBuilder, Action<IHostBuilder> builder) =>
            webHostBuilder.ConfigureServices(s => s.AddSingleton<IScriptHostBuilder>(_ => new DelegatedScriptJobHostBuilder(builder)));
    }
}

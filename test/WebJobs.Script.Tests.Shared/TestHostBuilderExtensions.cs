// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostBuilderExtensions
    {
        public static IHostBuilder ConfigureDefaultTestScriptHost(this IHostBuilder builder, Action<ScriptWebHostOptions> configure = null)
        {
            var webHostOptions = new ScriptWebHostOptions();

            configure?.Invoke(webHostOptions);

            var lifetime = new Mock<Microsoft.AspNetCore.Hosting.IApplicationLifetime>();

            return builder
                .ConfigureServices(s => s.AddSingleton<Microsoft.AspNetCore.Hosting.IApplicationLifetime>(lifetime.Object))
                .AddScriptHost(new OptionsWrapper<ScriptWebHostOptions>(webHostOptions));
        }
    }
}

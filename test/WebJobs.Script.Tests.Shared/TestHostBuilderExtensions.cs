// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostBuilderExtensions
    {
        public static IHostBuilder ConfigureDefaultTestScriptHost(this IHostBuilder builder, Action<ScriptWebHostOptions> configure = null)
        {
            var webHostOptions = new ScriptWebHostOptions();

            configure?.Invoke(webHostOptions);

            return builder.AddScriptHost(new OptionsWrapper<ScriptWebHostOptions>(webHostOptions));
        }
    }
}

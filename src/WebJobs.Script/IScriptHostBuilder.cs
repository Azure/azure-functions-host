// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IScriptHostBuilder
    {
        IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing);
    }

    public interface IScriptHostBuilderEx : IScriptHostBuilder
    {
        IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing, Action<IServiceCollection> configureServices);
    }
}

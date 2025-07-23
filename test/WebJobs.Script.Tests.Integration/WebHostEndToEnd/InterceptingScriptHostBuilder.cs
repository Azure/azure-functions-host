using System;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd;

internal class InterceptingScriptHostBuilder : IScriptHostBuilder
{
    private readonly DefaultScriptHostBuilder _builder;
    private readonly Func<IScriptHostBuilder, bool, bool, IHost> _interceptCallback;

    public InterceptingScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> appHostOptions, IServiceProvider rootServiceProvider, IServiceCollection rootServices, Func<IScriptHostBuilder, bool, bool, IHost> interceptCallback)
    {
        _builder = new DefaultScriptHostBuilder(appHostOptions, rootServices, rootServiceProvider);
        _interceptCallback = interceptCallback;
    }

    public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
    {
        return _interceptCallback(_builder, skipHostStartup, skipHostConfigurationParsing);
    }
}
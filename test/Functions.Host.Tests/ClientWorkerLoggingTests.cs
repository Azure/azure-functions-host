// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Host.Tests;

public class ClientWorkerLoggingTests
{
    private const string ClientCategory = "Azure.Functions.Rpc.Client.RpcClientScriptHostStartupCoordinator";

    [Theory]
    [InlineData(true, ClientCategory, LogLevel.Information, false, true)]
    [InlineData(true, ClientCategory, LogLevel.Debug, false, true)]
    [InlineData(true, ClientCategory, LogLevel.Trace, false, false)]
    [InlineData(true, ClientCategory, LogLevel.Trace, true, true)]
    [InlineData(true, ClientCategory, LogLevel.Warning, false, true)]
    [InlineData(true, ClientCategory, LogLevel.Error, false, true)]
    [InlineData(true, ClientCategory, LogLevel.Critical, false, true)]
    [InlineData(true, "Azure.Functions.Host.Controllers.WorkerLinkController", LogLevel.Information, false, true)]
    [InlineData(true, "Azure.FunctionsOther.Client", LogLevel.Error, false, false)]
    [InlineData(true, "System.Net.Http", LogLevel.Error, true, false)]
    [InlineData(true, "Host.Startup", LogLevel.Information, false, true)]
    [InlineData(true, "Worker.LanguageWorkerChannel.external.worker", LogLevel.Debug, false, true)]
    [InlineData(true, "Function.Hello.User", LogLevel.Error, false, false)]
    [InlineData(false, ClientCategory, LogLevel.Information, false, false)]
    [InlineData(false, ClientCategory, LogLevel.Critical, true, false)]
    [InlineData(false, "Azure.Functions.Host.Controllers.WorkerLinkController", LogLevel.Information, false, false)]
    [InlineData(false, "Host.Startup", LogLevel.Information, false, true)]
    public async Task SystemLogging_RespectsCompositionCategoryAndLevel(
        bool useClient, string category, LogLevel level, bool inDiagnosticMode, bool expected)
    {
        Mock<IEventGenerator> eventGenerator = new();
        IHost rootHost = CreateRootHost(useClient, inDiagnosticMode, eventGenerator.Object, out IServiceCollection rootServices);
        await using IAsyncDisposable rootLifetime = (IAsyncDisposable)rootHost;

        AssertSystemLog(rootHost.Services, eventGenerator, category, level, expected);

        await using ServiceProvider childProvider = CreateScriptHostProvider(rootHost.Services, rootServices);
        AssertSystemLog(childProvider, eventGenerator, category, level, expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClientComposition_PreservesCustomerLoggingFilters(bool configureCustomerLogging)
    {
        IConfiguration? loggingConfiguration = configureCustomerLogging
            ? new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LogLevel:Default"] = "Warning",
                ["LogLevel:Azure.Functions.Rpc.Client"] = "Error",
            }).Build()
            : null;

        Mock<IEventGenerator> eventGenerator = new();
        IHost rootHost = CreateRootHost(true, false, eventGenerator.Object, out IServiceCollection rootServices, loggingConfiguration);
        await using IAsyncDisposable rootLifetime = (IAsyncDisposable)rootHost;

        AssertCustomerLogging(rootHost.Services, configureCustomerLogging);
        AssertSystemLog(rootHost.Services, eventGenerator, ClientCategory, LogLevel.Information, expected: true);

        await using ServiceProvider childProvider = CreateScriptHostProvider(rootHost.Services, rootServices, loggingConfiguration);
        AssertCustomerLogging(childProvider, configureCustomerLogging);
        AssertSystemLog(childProvider, eventGenerator, ClientCategory, LogLevel.Information, expected: true);
    }

    private static void AssertSystemLog(
        IServiceProvider services, Mock<IEventGenerator> eventGenerator, string category, LogLevel level, bool expected)
    {
        ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
        eventGenerator.Invocations.Clear();

        logger.Log(level, "Client system message");

        eventGenerator.Verify(generator => generator.LogFunctionTraceEvent(
            level, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            category, It.IsAny<string>(), "Client system message", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>()), expected ? Times.Once() : Times.Never());
    }

    private static void AssertCustomerLogging(IServiceProvider services, bool configured)
    {
        Mock<ILogger> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        Mock<ILoggerProvider> provider = new();
        provider.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger.Object);

        using LoggerFactory factory = new([provider.Object], services.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>());
        ILogger clientLogger = factory.CreateLogger(ClientCategory);
        Assert.False(clientLogger.IsEnabled(LogLevel.Information));
        Assert.False(clientLogger.IsEnabled(LogLevel.Warning));
        Assert.Equal(configured, clientLogger.IsEnabled(LogLevel.Error));

        ILogger hostLogger = factory.CreateLogger("Host.Startup");
        Assert.Equal(!configured, hostLogger.IsEnabled(LogLevel.Information));
        Assert.True(hostLogger.IsEnabled(LogLevel.Warning));
    }

    private static ServiceProvider CreateScriptHostProvider(
        IServiceProvider rootProvider, IServiceCollection rootServices, IConfiguration? loggingConfiguration = null)
    {
        IServiceCollection childServices = rootProvider.CreateChildContainer(rootServices);
        childServices.AddLogging(logging =>
        {
            logging.AddDefaultWebJobsFilters();
            if (loggingConfiguration is not null)
            {
                logging.AddConfiguration(loggingConfiguration);
            }

            logging.AddWebJobsSystem<SystemLoggerProvider>();
            logging.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();
        });

        return childServices.BuildServiceProvider();
    }

    private static IHost CreateRootHost(
        bool useClient,
        bool inDiagnosticMode,
        IEventGenerator eventGenerator,
        out IServiceCollection services,
        IConfiguration? loggingConfiguration = null)
    {
        IServiceCollection capturedServices = null!;
        IConfiguration configuration = new ConfigurationBuilder().Build();
        IHost host = new HostBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddDefaultWebJobsFilters();
                    logging.AddWebJobsSystem<WebHostSystemLoggerProvider>();
                    if (loggingConfiguration is not null)
                    {
                        logging.AddConfiguration(loggingConfiguration);
                    }
                });
                webBuilder.ConfigureServices(rootServices =>
                {
                    capturedServices = rootServices;
                    rootServices.AddSingleton(configuration);
                    if (useClient)
                    {
                        rootServices.AddWebJobsScriptHost(configuration, ClientWorkerComposition.Instance);
                    }
                    else
                    {
                        rootServices.AddWebJobsScriptHost(configuration);
                    }

                    rootServices.AddSingleton(eventGenerator);
                    rootServices.AddSingleton(Mock.Of<IDebugStateProvider>(provider => provider.InDiagnosticMode == inDiagnosticMode));
                    rootServices.Configure<ScriptApplicationHostOptions>(options =>
                    {
                        options.LogPath = Path.GetTempPath();
                        options.ScriptPath = Directory.GetCurrentDirectory();
                        options.SecretsPath = Path.GetTempPath();
                    });
                });
                webBuilder.Configure(_ =>
                {
                });
            })
            .Build();

        services = capturedServices;
        return host;
    }
}

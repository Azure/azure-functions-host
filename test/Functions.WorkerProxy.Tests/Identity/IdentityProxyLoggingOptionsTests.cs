// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.Identity;

public sealed class IdentityProxyLoggingOptionsTests
{
    [Theory]
    [InlineData("Logging:Console:LogLevel:Default", "Information", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics", "Trace", "Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics.Child", "Trace",
        "Microsoft.AspNetCore.Hosting.Diagnostics.Child")]
    public void ProviderConfiguration_CannotEnableRequestUriLogs(string key, string value, string category)
    {
        using ServiceProvider provider = CreateProvider(new Dictionary<string, string?> { [key] = value });
        ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
        ILogger requests = factory.CreateLogger(category);

        Assert.False(requests.IsEnabled(LogLevel.Trace));
        Assert.False(requests.IsEnabled(LogLevel.Information));
        Assert.True(requests.IsEnabled(LogLevel.Warning));
        Assert.True(factory.CreateLogger("OtherCategory").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void FullyQualifiedProviderRule_CannotEnableRequestUriLogs()
    {
        using ServiceProvider provider = CreateProvider([], configure: logging =>
            logging.AddFilter<TestConsoleLoggerProvider>(IdentityProxyLoggingOptionsSetup.RequestCategory, LogLevel.Trace));
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));
    }

    [Theory]
    [InlineData("Error", true)]
    [InlineData("None", false)]
    public void ProviderConfiguration_StricterLevelIsPreserved(string level, bool errorEnabled)
    {
        using ServiceProvider provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Logging:Console:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics"] = level
        });
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.Equal(errorEnabled, logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void ExistingFilter_StillDecidesWhetherWarningsAreEnabled()
    {
        using ServiceProvider provider = CreateProvider([], configure: logging =>
            logging.AddFilter<TestConsoleLoggerProvider>(static (_, level) => level >= LogLevel.Error));
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void NoMatchingRule_PreservesTheOriginalMinimumLevel()
    {
        using ServiceProvider provider = CreateProvider([], configure: static logging => logging.SetMinimumLevel(LogLevel.Error));
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);

        Assert.False(logger.IsEnabled(LogLevel.Warning));
        Assert.True(logger.IsEnabled(LogLevel.Error));
    }

    [Fact]
    public void DisabledIdentityProxy_DoesNotChangeProviderConfiguration()
    {
        using ServiceProvider provider = CreateProvider(new Dictionary<string, string?>
        {
            ["Logging:Console:LogLevel:Default"] = "Information"
        }, enabled: false);
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void ConfigurationReload_CannotEnableProviderRequestUriLogs()
    {
        using ConfigurationManager configuration = new();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:Console:LogLevel:Default"] = "Warning"
        });
        using ServiceProvider provider = CreateProviderCore(configuration, enabled: true, configure: null);
        ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
        ILogger requests = factory.CreateLogger(IdentityProxyLoggingOptionsSetup.RequestCategory);
        Assert.False(factory.CreateLogger("OtherCategory").IsEnabled(LogLevel.Information));
        configuration["Logging:Console:LogLevel:Default"] = "Trace";

        ((IConfigurationRoot)configuration).Reload();

        Assert.False(requests.IsEnabled(LogLevel.Information));
        Assert.True(requests.IsEnabled(LogLevel.Warning));
        Assert.True(factory.CreateLogger("OtherCategory").IsEnabled(LogLevel.Information));
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> values, bool enabled = true,
        Action<ILoggingBuilder>? configure = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        return CreateProviderCore(configuration, enabled, configure);
    }

    private static ServiceProvider CreateProviderCore(IConfiguration configuration, bool enabled, Action<ILoggingBuilder>? configure)
    {
        ServiceCollection services = new();
        services.Configure<IdentityProxyOptions>(options => options.Enabled = enabled);
        services.AddSingleton<IPostConfigureOptions<LoggerFilterOptions>, IdentityProxyLoggingOptionsSetup>();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TestConsoleLoggerProvider());
            logging.AddConfiguration(configuration.GetSection("Logging"));
            configure?.Invoke(logging);
        });

        return services.BuildServiceProvider();
    }

    [ProviderAlias("Console")]
    private sealed class TestConsoleLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new TestLogger();

        public void Dispose()
        {
        }

        private sealed class TestLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class SystemLoggerProviderTests
    {
        private readonly IOptions<ScriptJobHostOptions> _options;
        private readonly IEnvironment _environment = new TestEnvironment();
        private readonly SystemLoggerProvider _provider;
        private bool _inDiagnosticMode;

        public SystemLoggerProviderTests()
        {
            var scriptOptions = new ScriptJobHostOptions
            {
                RootLogPath = Path.GetTempPath()
            };

            _options = new OptionsWrapper<ScriptJobHostOptions>(scriptOptions);

            var debugStateProvider = new Mock<IDebugStateProvider>(MockBehavior.Strict);
            debugStateProvider.Setup(p => p.InDiagnosticMode).Returns(() => _inDiagnosticMode);

            var appServiceOptions = new TestOptionsMonitor<AppServiceOptions>(new AppServiceOptions());
            _provider = new SystemLoggerProvider(_options, null, _environment, debugStateProvider.Object, null, appServiceOptions);
        }

        [Fact]
        public void CreateLogger_ReturnsSystemLogger_ForNonUserCategories()
        {
            Assert.IsType<SystemLogger>(_provider.CreateLogger(LogCategories.CreateFunctionCategory("TestFunction")));
            Assert.IsType<SystemLogger>(_provider.CreateLogger(ScriptConstants.LogCategoryHostGeneral));
            Assert.IsType<SystemLogger>(_provider.CreateLogger("NotAFunction.TestFunction.User"));
        }

        [Fact]
        public void CreateLogger_ReturnsNullLogger_ForUserCategory()
        {
            Assert.IsType<NullLogger>(_provider.CreateLogger(LogCategories.CreateFunctionUserCategory("TestFunction")));
        }

        [Fact]
        public void CreateLogger_DefaultsLogLevelToDebug()
        {
            var logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));
        }

        [Fact]
        public void CreateLogger_DiagnosticMode_LogsEverything()
        {
            var logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.False(logger.IsEnabled(LogLevel.Trace));

            _inDiagnosticMode = true;
            logger = _provider.CreateLogger(LogCategories.Startup);
            Assert.True(logger.IsEnabled(LogLevel.Information));
            Assert.True(logger.IsEnabled(LogLevel.Warning));
            Assert.True(logger.IsEnabled(LogLevel.Error));
            Assert.True(logger.IsEnabled(LogLevel.Critical));
            Assert.True(logger.IsEnabled(LogLevel.Debug));
            Assert.True(logger.IsEnabled(LogLevel.Trace));
        }

        [Fact]
        public void VerifySystemLogFiltering()
        {
            var testLoggerProvider = new TestLoggerProvider();
            var testLoggerFactory = new LoggerFactory();
            testLoggerFactory.AddProvider(testLoggerProvider);

            var testEventGenerator = new TestEventGenerator();

            var builder = Program.CreateWebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(testLoggerProvider);

                    b.SetMinimumLevel(LogLevel.Trace);
                })
                .ConfigureServices((bc, s) =>
                {
                    s.AddSingleton<IEventGenerator>(testEventGenerator);
                });

            var host = builder.Build();

            var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();

            var serviceProvider = host.Services;
            string systemOnlyLog = "Should only be logged to system!";
            ILogger logger = loggerFactory.CreateLogger("Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption.LinuxConsumptionMetricsTracker");
            logger.LogError(systemOnlyLog);
            logger.LogWarning(systemOnlyLog);
            logger.LogInformation(systemOnlyLog);
            logger.LogDebug(systemOnlyLog);
            logger.LogTrace(systemOnlyLog);

            logger = loggerFactory.CreateLogger("Microsoft.Azure.WebJobs.Script.WebHost.Metrics.LinuxContainerLegionMetricsPublisher");
            logger.LogError(systemOnlyLog);
            logger.LogWarning(systemOnlyLog);
            logger.LogInformation(systemOnlyLog);
            logger.LogDebug(systemOnlyLog);
            logger.LogTrace(systemOnlyLog);

            string allLog = "Should be logged to all.";
            logger = loggerFactory.CreateLogger(typeof(SomeOtherClass).FullName);
            logger.LogError(allLog);
            logger.LogWarning(allLog);
            logger.LogInformation(allLog);
            logger.LogDebug(allLog);
            logger.LogTrace(allLog);

            // expected logs are filtered from non-system providers
            var logs = testLoggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(3, logs.Length);
            Assert.All(logs, l => Assert.Equal(allLog, l.FormattedMessage));

            // expect all to be logged to system logs (all logs Debug or greater in level)
            // SystemLogs are hardcoded to Debug so we only expect Debug level or greater
            var systemLogs = testEventGenerator.GetFunctionTraceEvents().ToArray();
            Assert.Equal(12, systemLogs.Length);
        }

        private class SomeOtherClass
        {
        }
    }
}
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class DefaultDependencyValidatorTests
    {
        [Fact]
        public async Task Validator_AllValid()
        {
            LogMessage invalidServicesMessage = await RunTest();

            string msg = $"If you have registered new dependencies, make sure to update the DependencyValidator. Invalid Services:{Environment.NewLine}";
            Assert.True(invalidServicesMessage == null, msg + invalidServicesMessage?.Exception?.ToString());
        }

        [Fact]
        public async Task Validator_InvalidServices_LogsError()
        {
            LogMessage invalidServicesMessage = await RunTest(configureJobHost: s =>
            {
                s.AddSingleton<IHostedService, MyHostedService>();
                s.AddSingleton<IScriptEventManager, MyScriptEventManager>();
                s.AddSingleton<IMetricsLogger>(new MyMetricsLogger());

                // Try removing system logger
                var descriptor = s.Single(p => p.ImplementationType == typeof(SystemLoggerProvider));
                s.Remove(descriptor);
            });

            Assert.NotNull(invalidServicesMessage);

            IEnumerable<string> messageLines = invalidServicesMessage.Exception.Message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim());
            Assert.Equal(5, messageLines.Count());
            Assert.Contains(messageLines, p => p.StartsWith("[Invalid]") && p.EndsWith(nameof(MyHostedService)));
            Assert.Contains(messageLines, p => p.StartsWith("[Invalid]") && p.EndsWith(nameof(MyScriptEventManager)));
            Assert.Contains(messageLines, p => p.StartsWith("[Invalid]") && p.EndsWith(nameof(MyMetricsLogger)));
            Assert.Contains(messageLines, p => p.StartsWith("[Missing]") && p.EndsWith(nameof(SystemLoggerProvider)));
        }

        [Fact]
        public async Task Validator_NoJobHost()
        {
            LogMessage invalidServicesMessage = await RunTest(configureWebHost: s =>
            {
                // This will force us to skip host startup (which removes the JobHost)
                s.AddSingleton<IScriptHostBuilder, TestScriptHostBuilder>();
            });

            string msg = $"If you have registered new dependencies, make sure to update the DependencyValidator. Invalid Services:{Environment.NewLine}";
            Assert.True(invalidServicesMessage == null, msg + invalidServicesMessage?.Exception?.ToString());
        }

        private async Task<LogMessage> RunTest(Action<IServiceCollection> configureWebHost = null, Action<IServiceCollection> configureJobHost = null)
        {
            LogMessage invalidServicesMessage = null;
            TestLoggerProvider loggerProvider = new TestLoggerProvider();

            var builder = Program.CreateWebHostBuilder(null)
                    .ConfigureLogging(b =>
                    {
                        b.AddProvider(loggerProvider);
                    })
                    .ConfigureServices(s =>
                    {
                        string uniqueTestRootPath = Path.Combine(Path.GetTempPath(), "FunctionsTest", "DependencyValidatorTests");
                        s.PostConfigureAll<ScriptApplicationHostOptions>(o =>
                        {
                            o.IsSelfHost = true;
                            o.LogPath = Path.Combine(uniqueTestRootPath, "logs");
                            o.SecretsPath = Path.Combine(uniqueTestRootPath, "secrets");
                            o.ScriptPath = uniqueTestRootPath;
                        });

                        configureWebHost?.Invoke(s);
                    })
                    .ConfigureScriptHostLogging(b =>
                    {
                        b.AddProvider(loggerProvider);
                    })
                    .ConfigureScriptHostServices(b =>
                    {
                        configureJobHost?.Invoke(b);
                    });

            using (var host = builder.Build())
            {
                await host.StartAsync();

                await TestHelpers.Await(() =>
                {
                    return loggerProvider.GetAllLogMessages()
                        .FirstOrDefault(p => p.FormattedMessage.StartsWith("Host initialization")) != null;
                }, userMessageCallback: () => loggerProvider.GetLog());

                invalidServicesMessage = loggerProvider.GetAllLogMessages()
                   .FirstOrDefault(m => m.Category.EndsWith(nameof(DependencyValidator)));

                await host.StopAsync();
            }

            return invalidServicesMessage;
        }

        private class MyHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        private class MyScriptEventManager : IScriptEventManager
        {
            public void Publish(ScriptEvent scriptEvent)
            {
            }

            public IDisposable Subscribe(IObserver<ScriptEvent> observer)
            {
                return null;
            }
        }

        private class MyMetricsLogger : IMetricsLogger
        {
            public object BeginEvent(string eventName, string functionName = null, string data = null) => null;

            public void BeginEvent(MetricEvent metricEvent)
            {
            }

            public void EndEvent(MetricEvent metricEvent)
            {
            }

            public void EndEvent(object eventHandle)
            {
            }

            public void LogEvent(MetricEvent metricEvent)
            {
            }

            public void LogEvent(string eventName, string functionName = null, string data = null)
            {
            }
        }

        private class TestScriptHostBuilder : IScriptHostBuilder
        {
            private readonly DefaultScriptHostBuilder _builder;

            public TestScriptHostBuilder(IOptionsMonitor<ScriptApplicationHostOptions> appHostOptions, IServiceProvider rootServiceProvider, IServiceScopeFactory rootServiceScopeFactory)
            {
                _builder = new DefaultScriptHostBuilder(appHostOptions, rootServiceProvider, rootServiceScopeFactory);
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                // always skip host startup
                return _builder.BuildHost(true, false);
            }
        }
    }
}

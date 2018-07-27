// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Host
{
    public class WebScriptHostManagerTimeoutTests : IDisposable
    {
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();
        
        [Fact(Skip = "Investigate test failure")]
        public async Task OnTimeoutException_IgnoreToken_StopsManager()
        {
            IHost host = null;
            try
            {
                IScriptJobHost scriptHost;
                IScriptHostManager jobHostManager;
                (host, scriptHost, jobHostManager) = await RunTimeoutExceptionTest(handleCancellation: false);
                
                await TestHelpers.Await(() => !(jobHostManager.State == ScriptHostState.Running), userMessageCallback: () => "Expected host to not be running");

                var traces = _loggerProvider.GetAllLogMessages();
                Assert.DoesNotContain(traces, t => t.FormattedMessage.StartsWith("Done"));
                Assert.Contains(traces, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
                Assert.Contains(traces, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
            }
            finally
            {
                host?.Dispose();
            }
        }

        [Fact(Skip = "Investigate test failure")]
        public async Task OnTimeoutException_UsesToken_ManagerKeepsRunning()
        {
            IHost host = null;
            try
            {
                IScriptJobHost scriptHost;
                IScriptHostManager jobHostManager;
                (host, scriptHost, jobHostManager) = await RunTimeoutExceptionTest(handleCancellation: true);

                // wait a few seconds to make sure the manager doesn't die
                await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(jobHostManager.State == ScriptHostState.Running),
                timeout: 3000, throwWhenDebugging: true, userMessageCallback: () => "Expected host manager not to die"));

                var messages = _loggerProvider.GetAllLogMessages();
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Done"));
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
                Assert.DoesNotContain(messages, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
            }
            finally
            {
                host?.Dispose();
            }
        }

        private async Task<(IHost, IScriptJobHost, IScriptHostManager)> RunTimeoutExceptionTest(bool handleCancellation)
        {
            TimeSpan gracePeriod = TimeSpan.FromMilliseconds(5000);
            var (host, jobhost, jobHostManager) = await CreateAndStartWebScriptHost();

                string scenarioName = handleCancellation ? "useToken" : "ignoreToken";

            var args = new Dictionary<string, object>
            {
                { "input", scenarioName }
            };

            await Assert.ThrowsAsync<FunctionTimeoutException>(() => jobhost.CallAsync("TimeoutToken", args));

            return (host, jobhost, jobHostManager);
        }

        private async Task<(IHost, IScriptJobHost, IScriptHostManager)> CreateAndStartWebScriptHost()
        {
            var functions = new Collection<string> { "TimeoutToken" };

            var options = new ScriptHostOptions()
            {
                RootScriptPath = $@"TestScripts\CSharp",
                FileLoggingMode = FileLoggingMode.Always,
                Functions = functions,
                FunctionTimeout = TimeSpan.FromSeconds(3)
            };

            var loggerProviderFactory = new TestLoggerProviderFactory(_loggerProvider);
            var mockEventManager = new Mock<IScriptEventManager>();
            var mockRouter = new Mock<IWebJobsRouter>();

            var host = new HostBuilder()
                .ConfigureDefaultTestScriptHost()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IWebJobsRouter>(mockRouter.Object);
                    s.AddSingleton<ISecretManagerFactory>(new TestSecretManagerFactory());
                    s.AddSingleton<IScriptEventManager>(mockEventManager.Object);
                    s.AddSingleton<IOptions<ScriptHostOptions>>(new OptionsWrapper<ScriptHostOptions>(options));
                    s.AddSingleton<IOptions<ScriptWebHostOptions>>(new OptionsWrapper<ScriptWebHostOptions>(new ScriptWebHostOptions { SecretsPath = _secretsDirectory.Path }));
                    s.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
                    s.AddSingleton(ScriptSettingsManager.Instance);
                })
                .Build();


            await host.StartAsync();

            var manager = host.Services.GetService<IScriptHostManager>();
            await TestHelpers.Await(() => manager.State == ScriptHostState.Running, userMessageCallback: () => "Expected host to be running");

            IScriptJobHost scriptHost = host.GetScriptHost();

            return (host, scriptHost, manager);
        }

        public void Dispose()
        {
            _secretsDirectory.Dispose();
        }
    }
}

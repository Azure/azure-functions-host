// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Host
{
    public class WebScriptHostManagerTimeoutTests
    {
        private TestDisposable _disposedService = new TestDisposable();

        [Fact]
        public async Task OnTimeoutException_IgnoreToken_StopsManager()
        {
            using (var host = await RunTimeoutExceptionTest(handleCancellation: false))
            {
                var jobHostManager = host.WebHostServices.GetService<IScriptHostManager>();

                await TestHelpers.Await(() => !(jobHostManager.State == ScriptHostState.Running), userMessageCallback: () => "Expected host to not be running");

                var messages = host.GetScriptHostLogMessages().Where(t => t?.FormattedMessage != null);
                Assert.DoesNotContain(messages, t => t.FormattedMessage.StartsWith("Done"));
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
                Assert.Contains(messages, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
            }

            // Validates a bug where WebHost services were not being disposed on a function timeout
            Assert.True(_disposedService.IsDisposed, "Expected services to be disposed");
        }

        [Fact]
        public async Task OnTimeoutException_UsesToken_ManagerKeepsRunning()
        {
            using (var host = await RunTimeoutExceptionTest(handleCancellation: true))
            {
                var jobHostManager = host.WebHostServices.GetService<IScriptHostManager>();

                // wait a few seconds to make sure the manager doesn't die
                await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(jobHostManager.State == ScriptHostState.Running),
                timeout: 3000, throwWhenDebugging: true, userMessageCallback: () => "Expected host manager not to die"));

                var messages = host.GetScriptHostLogMessages().Where(t => t?.FormattedMessage != null);
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Done"));
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
                Assert.DoesNotContain(messages, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
            }
        }

        [Fact]
        public async Task OnTimeoutException_OOP_HasExpectedLogs()
        {
            Environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionWorkerRuntime, "node");
            using (var host = await RunTimeoutExceptionTest(handleCancellation: false, timeoutFunctionName: "TimeoutSync", path: @"TestScripts\Node"))
            {
                var jobHostManager = host.WebHostServices.GetService<IScriptHostManager>();

                // wait a few seconds to make sure the manager doesn't die
                await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(jobHostManager.State == ScriptHostState.Running),
                timeout: 3000, throwWhenDebugging: true, userMessageCallback: () => "Expected host manager not to die"));

                var messages = host.GetScriptHostLogMessages().Where(t => t?.FormattedMessage != null);
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("A function timeout has occurred. Restarting worker process executing invocationId "));
                Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Restarting channel"));
                Assert.Contains(messages, t => t.FormattedMessage == "Restart of language worker process(es) completed.");
            }
        }

        private async Task<TestFunctionHost> RunTimeoutExceptionTest(bool handleCancellation, string timeoutFunctionName = "TimeoutToken", string path = @"TestScripts\CSharp")
        {
            TimeSpan gracePeriod = TimeSpan.FromMilliseconds(5000);
            var host = CreateAndStartWebScriptHost(timeoutFunctionName, path);

            string scenarioName = handleCancellation ? "useToken" : "ignoreToken";

            var args = new Dictionary<string, object>
            {
                { "input", scenarioName }
            };

            var jobHost = host.JobHostServices.GetService<IJobHost>();

            await Assert.ThrowsAsync<FunctionTimeoutException>(() => jobHost.CallAsync(timeoutFunctionName, args));

            return host;
        }

        private TestFunctionHost CreateAndStartWebScriptHost(string timeoutFunctionName, string path)
        {
            var functions = new Collection<string> { timeoutFunctionName };

            return new TestFunctionHost(
                 path,
                 Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions"),
                 configureWebHostServices: b =>
                 {
                     b.AddSingleton<IHostedService>(_ => _disposedService);
                 },
                 configureScriptHostWebJobsBuilder: b =>
                 {
                     b.Services.Configure<ScriptJobHostOptions>(o =>
                     {
                         o.Functions = functions;
                         o.FunctionTimeout = TimeSpan.FromSeconds(3);
                     });
                 });
        }

        private class TestDisposable : IHostedService, IDisposable
        {
            public bool IsDisposed { get; private set; } = false;

            public void Dispose()
            {
                IsDisposed = true;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }
    }
}

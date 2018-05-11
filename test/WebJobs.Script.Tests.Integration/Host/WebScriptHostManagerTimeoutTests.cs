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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Host
{
    public class WebScriptHostManagerTimeoutTests : IDisposable
    {
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private WebScriptHostManager _manager;
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        [Fact(Skip = "Investigate test failure")]
        public async Task OnTimeoutException_IgnoreToken_StopsManager()
        {
            await RunTimeoutExceptionTest(handleCancellation: false);

            await TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running), userMessageCallback: () => "Expected host to not be running");

            var traces = _loggerProvider.GetAllLogMessages();
            Assert.DoesNotContain(traces, t => t.FormattedMessage.StartsWith("Done"));
            Assert.Contains(traces, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.Contains(traces, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
        }

        [Fact(Skip = "Investigate test failure")]
        public async Task OnTimeoutException_UsesToken_ManagerKeepsRunning()
        {
            await RunTimeoutExceptionTest(handleCancellation: true);

            // wait a few seconds to make sure the manager doesn't die
            await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running),
                timeout: 3000, throwWhenDebugging: true, userMessageCallback: () => "Expected host manager not to die"));

            var messages = _loggerProvider.GetAllLogMessages();
            Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Done"));
            Assert.Contains(messages, t => t.FormattedMessage.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.DoesNotContain(messages, t => t.FormattedMessage == "A function timeout has occurred. Host is shutting down.");
        }


        private async Task RunTimeoutExceptionTest(bool handleCancellation)
        {
            TimeSpan gracePeriod = TimeSpan.FromMilliseconds(5000);
            _manager = await CreateAndStartWebScriptHostManager();

            string scenarioName = handleCancellation ? "useToken" : "ignoreToken";

            var args = new Dictionary<string, object>
            {
                { "input", scenarioName }
            };

            await Assert.ThrowsAsync<FunctionTimeoutException>(() => _manager.Instance.CallAsync("TimeoutToken", args));
        }

        private async Task<WebScriptHostManager> CreateAndStartWebScriptHostManager()
        {
            var functions = new Collection<string> { "TimeoutToken" };

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = $@"TestScripts\CSharp",
                FileLoggingMode = FileLoggingMode.Always,
                Functions = functions,
                FunctionTimeout = TimeSpan.FromSeconds(3)
            };

            var loggerProviderFactory = new TestLoggerProviderFactory(_loggerProvider);
            var mockEventManager = new Mock<IScriptEventManager>();
            var mockRouter = new Mock<IWebJobsRouter>();
            var manager = new WebScriptHostManager(
                config,
                new TestSecretManagerFactory(),
                mockEventManager.Object,
                ScriptSettingsManager.Instance,
                new WebHostSettings { SecretsPath = _secretsDirectory.Path },
                mockRouter.Object,
                NullLoggerFactory.Instance);

            Task task = Task.Run(() => { manager.RunAndBlock(); });
            await TestHelpers.Await(() => manager.State == ScriptHostState.Running, userMessageCallback: () => "Expected host to be running");

            return manager;
        }

        public void Dispose()
        {
            if (_manager != null)
            {
                _manager.Stop();
                _manager.Dispose();
            }

            _secretsDirectory.Dispose();
        }
    }
}

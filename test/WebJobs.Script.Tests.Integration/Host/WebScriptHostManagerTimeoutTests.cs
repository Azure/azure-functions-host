// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Host
{
    public class WebScriptHostManagerTimeoutTests : IDisposable
    {
        private readonly TempDirectory _secretsDirectory = new TempDirectory();
        private WebScriptHostManager _manager;

        [Fact]
        public async Task OnTimeoutException_IgnoreToken_StopsManager()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            await RunTimeoutExceptionTest(trace, handleCancellation: false);

            await TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running));
            Assert.DoesNotContain(trace.Traces, t => t.Message.StartsWith("Done"));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.Contains(trace.Traces, t => t.Message == "A function timeout has occurred. Host is shutting down.");
        }

        [Fact]
        public async Task OnTimeoutException_UsesToken_ManagerKeepsRunning()
        {
            var trace = new TestTraceWriter(TraceLevel.Info);

            await RunTimeoutExceptionTest(trace, handleCancellation: true);

            // wait a few seconds to make sure the manager doesn't die
            await Assert.ThrowsAsync<ApplicationException>(() => TestHelpers.Await(() => !(_manager.State == ScriptHostState.Running),
                timeout: 3000, throwWhenDebugging: true));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Done"));
            Assert.Contains(trace.Traces, t => t.Message.StartsWith("Timeout value of 00:00:03 exceeded by function 'Functions.TimeoutToken' (Id: "));
            Assert.DoesNotContain(trace.Traces, t => t.Message == "A function timeout has occurred. Host is shutting down.");
        }

        private async Task RunTimeoutExceptionTest(TraceWriter trace, bool handleCancellation)
        {
            TimeSpan gracePeriod = TimeSpan.FromMilliseconds(5000);
            _manager = await CreateAndStartWebScriptHostManager(trace);

            string scenarioName = handleCancellation ? "useToken" : "ignoreToken";

            var args = new Dictionary<string, object>
            {
                { "input", scenarioName }
            };

            await Assert.ThrowsAsync<FunctionTimeoutException>(() => _manager.Instance.CallAsync("TimeoutToken", args));
        }

        private async Task<WebScriptHostManager> CreateAndStartWebScriptHostManager(TraceWriter traceWriter)
        {
            var functions = new Collection<string> { "TimeoutToken" };

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = $@"TestScripts\CSharp",
                TraceWriter = traceWriter,
                FileLoggingMode = FileLoggingMode.Always,
                Functions = functions,
                FunctionTimeout = TimeSpan.FromSeconds(3)
            };

            var mockEventManager = new Mock<IScriptEventManager>();
            var manager = new WebScriptHostManager(config, new TestSecretManagerFactory(), mockEventManager.Object, ScriptSettingsManager.Instance, new WebHostSettings { SecretsPath = _secretsDirectory.Path });
            Task task = Task.Run(() => { manager.RunAndBlock(); });
            await TestHelpers.Await(() => manager.State == ScriptHostState.Running);

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

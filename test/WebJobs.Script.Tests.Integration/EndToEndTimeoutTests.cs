// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    [Trait("E2E", nameof(EndToEndTimeoutTests))]
    public class EndToEndTimeoutTests
    {
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;

        [Fact]
        public async Task TimeoutTest_SyncFunction_Node()
        {
            await TimeoutTest_SyncFunction("Node");
        }

        [Fact]
        public async Task TimeoutTest_SyncFunction_Bash()
        {
            await TimeoutTest_SyncFunction("Bash");
        }

        [Fact]
        public async Task TimeoutTest_SyncFunction_Batch()
        {
            await TimeoutTest_SyncFunction("WindowsBatch");
        }

        [Fact]
        public async Task TimeoutTest_SyncFunction_Python()
        {
            await TimeoutTest_SyncFunction("Python");
        }

        [Fact]
        public async Task TimeoutTest_SyncFunction_Powershell()
        {
            await TimeoutTest_SyncFunction("PowerShell");
        }

        [Fact]
        public async Task TimeoutTest_SyncFunction_CSharp()
        {
            await TimeoutTest_SyncFunction("CSharp");
        }

        private async Task TimeoutTest_SyncFunction(string scriptLang)
        {
            await RunTimeoutTest(scriptLang, "TimeoutSync");
        }

        [Fact]
        public async Task TimeoutTest_UsingToken_CSharp()
        {
            await RunTokenTest("useToken", async (logs) =>
             {
                 // The function should 'clean up' and write 'Done'
                 await TestHelpers.Await(() =>
                 {
                     var doneLog = logs.SingleOrDefault(l => l.EndsWith("Done"));
                     return doneLog != null;
                 });
             });
        }

        [Fact]
        public async Task TimeoutTest_IgnoringToken_CSharp()
        {
            await RunTokenTest("ignoreToken", (logs) =>
             {
                 // We do not expect 'Done' to be written here.
                 Assert.False(logs.Any(l => l.EndsWith("Done")));
             });
        }

        private async Task RunTokenTest(string scenario, Action<IEnumerable<string>> verify)
        {
            string functionName = "TimeoutToken";
            TestHelpers.ClearFunctionLogs(functionName);
            var traceWriter = new TestTraceWriter(TraceLevel.Info);
            using (var manager = await CreateAndStartScriptHostManager("CSharp", functionName, TimeSpan.FromSeconds(3), traceWriter))
            {
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", scenario },
                };

                FunctionTimeoutException ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => manager.Instance.CallAsync(functionName, arguments));

                var exception = GetExceptionHandler(manager).TimeoutExceptionInfos.Single().SourceException;
                Assert.IsType<FunctionTimeoutException>(exception);

                verify(traceWriter.Traces.Select(t => t.ToString()));
            }
        }

        private async Task RunTimeoutTest(string scriptLang, string functionName)
        {
            TestHelpers.ClearFunctionLogs(functionName);
            TimeSpan testTimeout = TimeSpan.FromSeconds(3);
            var traceWriter = new TestTraceWriter(TraceLevel.Verbose);
            using (var manager = await CreateAndStartScriptHostManager(scriptLang, functionName, testTimeout, traceWriter))
            {
                string testData = Guid.NewGuid().ToString();

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "inputData", testData },
                };

                FunctionTimeoutException ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => manager.Instance.CallAsync(functionName, arguments));

                await TestHelpers.Await(() =>
                {
                    // make sure logging from within the function worked
                    // TODO: This doesn't appear to work for Powershell in AppVeyor. Need to investigate.
                    // bool hasTestData = inProgressLogs.Any(l => l.Contains(testData));
                    var expectedMessage = $"Timeout value of {testTimeout} was exceeded by function: Functions.{functionName}";
                    var traces = string.Join(Environment.NewLine, traceWriter.Traces);
                    return traces.Contains(expectedMessage);
                });

                var exception = GetExceptionHandler(manager).TimeoutExceptionInfos.Single().SourceException;
                Assert.IsType<FunctionTimeoutException>(exception);
            }
        }

        private MockExceptionHandler GetExceptionHandler(ScriptHostManager manager)
        {
            return manager.Instance.ScriptConfig.HostConfig.GetService<IWebJobsExceptionHandler>() as MockExceptionHandler;
        }

        private async Task<MockScriptHostManager> CreateAndStartScriptHostManager(string scriptLang, string functionName, TimeSpan timeout, TraceWriter traceWriter)
        {
            var functions = new Collection<string>();
            functions.Add(functionName);

            ScriptHostConfiguration config = new ScriptHostConfiguration()
            {
                RootScriptPath = $@"TestScripts\{scriptLang}",
                TraceWriter = traceWriter,
                FileLoggingMode = FileLoggingMode.Always,
                Functions = functions,
                FunctionTimeout = timeout
            };

            var scriptHostManager = new MockScriptHostManager(config);
            ThreadPool.QueueUserWorkItem((s) => scriptHostManager.RunAndBlock());
            await TestHelpers.Await(() => scriptHostManager.State == ScriptHostState.Running);

            return scriptHostManager;
        }

        private class MockExceptionHandler : IWebJobsExceptionHandler
        {
            public ICollection<ExceptionDispatchInfo> UnhandledExceptionInfos { get; } = new Collection<ExceptionDispatchInfo>();

            public ICollection<ExceptionDispatchInfo> TimeoutExceptionInfos { get; } = new Collection<ExceptionDispatchInfo>();

            public void Initialize(JobHost host)
            {
            }

            public Task OnTimeoutExceptionAsync(ExceptionDispatchInfo exceptionInfo, TimeSpan timeoutGracePeriod)
            {
                TimeoutExceptionInfos.Add(exceptionInfo);
                return Task.CompletedTask;
            }

            public Task OnUnhandledExceptionAsync(ExceptionDispatchInfo exceptionInfo)
            {
                UnhandledExceptionInfos.Add(exceptionInfo);
                return Task.CompletedTask;
            }
        }

        private class MockScriptHostManager : ScriptHostManager
        {
            public MockScriptHostManager(ScriptHostConfiguration config)
                : base(config, new Mock<IScriptEventManager>().Object)
            {
            }

            public MockScriptHostManager(ScriptHostConfiguration config, IScriptEventManager eventManager)
                : base(config, eventManager)
            {
            }

            protected override void OnInitializeConfig(ScriptHostConfiguration config)
            {
                base.OnInitializeConfig(config);
                config.HostConfig.AddService<IWebJobsExceptionHandler>(new MockExceptionHandler());
            }
        }
    }
}

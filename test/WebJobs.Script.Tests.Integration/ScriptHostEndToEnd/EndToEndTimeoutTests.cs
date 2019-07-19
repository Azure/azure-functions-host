// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.EndToEnd
{
    public class EndToEndTimeoutTests
    {
        private static readonly ScriptSettingsManager SettingsManager = ScriptSettingsManager.Instance;
        private TestLoggerProvider _loggerProvider = new TestLoggerProvider();

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
                 Assert.NotEmpty(logs);
                 Assert.False(logs.Any(l => l.EndsWith("Done")));
             });
        }

        private async Task RunTokenTest(string scenario, Action<IEnumerable<string>> verify)
        {
            string functionName = "TimeoutToken";
            TestHelpers.ClearFunctionLogs(functionName);
            IHostBuilder builder = CreateTimeoutHostBuilder(@"TestScripts\CSharp", TimeSpan.FromSeconds(3), functionName);
            using (var host = builder.Build())
            {
                await host.StartAsync();
                var scriptHost = host.GetScriptHost();
                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "input", scenario },
                };

                FunctionTimeoutException ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => scriptHost.CallAsync(functionName, arguments));

                var exception = GetExceptionHandler(host).TimeoutExceptionInfos.Single().SourceException;
                Assert.IsType<FunctionTimeoutException>(exception);

                verify(_loggerProvider.GetAllLogMessages().Where(t => t.FormattedMessage != null).Select(t => t.FormattedMessage));
            }
        }

        private async Task RunTimeoutTest(string scriptLang, string functionName)
        {
            TestHelpers.ClearFunctionLogs(functionName);
            TimeSpan testTimeout = TimeSpan.FromSeconds(3);
            IHostBuilder builder = CreateTimeoutHostBuilder($@"TestScripts\{scriptLang}", testTimeout, functionName);
            using (var host = builder.Build())
            {
                await host.StartAsync();
                ScriptHost scriptHost = host.GetScriptHost();
                string testData = Guid.NewGuid().ToString();

                Dictionary<string, object> arguments = new Dictionary<string, object>
                {
                    { "inputData", testData },
                };

                FunctionTimeoutException ex = await Assert.ThrowsAsync<FunctionTimeoutException>(() => scriptHost.CallAsync(functionName, arguments));

                await TestHelpers.Await(() =>
                {
                    // make sure logging from within the function worked
                    // TODO: This doesn't appear to work for Powershell in AppVeyor. Need to investigate.
                    // bool hasTestData = inProgressLogs.Any(l => l.Contains(testData));
                    var expectedMessage = $"Timeout value of {testTimeout} exceeded by function 'Functions.{functionName}'";
                    var traces = string.Join(Environment.NewLine, _loggerProvider.GetAllLogMessages().Where(t => t.FormattedMessage != null).Select(p => p.FormattedMessage));
                    return traces.Contains(expectedMessage);
                });

                var exception = GetExceptionHandler(host).TimeoutExceptionInfos.Single().SourceException;
                Assert.IsType<FunctionTimeoutException>(exception);
            }
        }

        private IHostBuilder CreateTimeoutHostBuilder(string scriptPath, TimeSpan timeout, string functionName)
        {
            var builder = new HostBuilder()
               .ConfigureDefaultTestWebScriptHost(b =>
               {
                   b.Services.Configure<ScriptJobHostOptions>(o =>
                   {
                       o.FunctionTimeout = timeout;
                       o.Functions = new List<string> { functionName };
                   });
               },
               options =>
               {
                   options.ScriptPath = scriptPath;
               }, runStartupHostedServices: true)
               .ConfigureLogging(b =>
               {
                   b.AddProvider(_loggerProvider);
               })
               .ConfigureServices(s =>
               {
                   s.AddSingleton<IWebJobsExceptionHandler, MockExceptionHandler>();
               });

            return builder;
        }

        private MockExceptionHandler GetExceptionHandler(IHost host)
        {
            return host.Services.GetService<IWebJobsExceptionHandler>() as MockExceptionHandler;
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
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Tests.Integration;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Rpc
{
    /// <summary>
    /// End-to-end coverage for the language-worker restart-retry shutdown path. When the
    /// dispatcher exceeds its restart-retry threshold and has no jobhost worker channels left,
    /// it calls <see cref="IScriptApplicationLifetime.StopApplication"/>. That call must reach
    /// the outer WebHost's <see cref="IHostApplicationLifetime"/>; if it lands on the inner
    /// generic host's lifetime instead, the process never recycles.
    /// </summary>
    [Trait(TestTraits.Group, TestTraits.NonE2EControllers)]
    public class FunctionDispatcherShutdownEndToEndTests
    {
        private readonly string _testScriptPath = @"TestScripts\Node";
        private readonly string _testLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");

        [Fact]
        public async Task FunctionDispatcher_ExceededRestartRetryCount_StopsOuterWebHost()
        {
            using var testHost = new TestFunctionHost(_testScriptPath, _testLogPath);

            RpcFunctionInvocationDispatcher dispatcher = null;
            await TestHelpers.Await(() =>
            {
                var factory = testHost.JobHostServices.GetService<IFunctionInvocationDispatcherFactory>();
                dispatcher = factory?.GetFunctionDispatcher() as RpcFunctionInvocationDispatcher;
                return dispatcher?.State == FunctionInvocationDispatcherState.Initialized
                       && dispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Count() == 1;
            }, pollingInterval: 500, timeout: 120 * 1000, userMessageCallback: () => "Dispatcher did not reach Initialized state with exactly one Node jobhost worker channel.");

            // Pre-load the error stack so the next published WorkerErrorEvent tips past the
            // threshold. Using fresh timestamps avoids AddOrUpdateErrorBucket's stale-stack drain.
            int threshold = dispatcher.ErrorEventsThreshold;
            for (int i = 0; i < threshold; i++)
            {
                dispatcher.LanguageWorkerErrors.Push(
                    new WorkerErrorEvent(RpcWorkerConstants.NodeLanguageWorkerName, $"primer-{i}", new InvalidOperationException("primer")));
            }

            var outerLifetime = testHost.WebHostServices.GetRequiredService<IHostApplicationLifetime>();
            var outerStopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = outerLifetime.ApplicationStopping.Register(() => outerStopping.TrySetResult());

            string liveWorkerId = dispatcher.JobHostLanguageWorkerChannelManager.GetChannels().Single().Id;
            var eventManager = testHost.JobHostServices.GetRequiredService<IScriptEventManager>();
            eventManager.Publish(new WorkerErrorEvent(
                RpcWorkerConstants.NodeLanguageWorkerName, liveWorkerId, new InvalidOperationException("simulated worker failure exceeding restart retry count")));

            await outerStopping.Task.TestWaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(outerLifetime.ApplicationStopping.IsCancellationRequested);
        }
    }
}

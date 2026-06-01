// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Host
{
    /// <summary>
    /// Regression tests for the lifetime contract resolved by inner (script host) DI
    /// consumers. Components inside the script host that call <c>StopApplication()</c>
    /// to recycle the process must reach the outer web host's lifetime, not the inner
    /// generic host's no-op lifetime.
    /// </summary>
    [Trait(TestTraits.Group, TestTraits.NonE2EControllers)]
    public class ScriptApplicationLifetimeTests
    {
        private readonly string _testScriptPath = @"TestScripts\CSharp";
        private readonly string _testLogPath = Path.Combine(TestHelpers.FunctionsTestDirectory, "Logs", Guid.NewGuid().ToString(), @"Functions");

        [Fact]
        public async Task IScriptApplicationLifetime_ResolvedFromJobHostServices_StopsOuterWebHost()
        {
            using var testHost = new TestFunctionHost(_testScriptPath, _testLogPath);

            var innerLifetime = testHost.JobHostServices.GetRequiredService<IScriptApplicationLifetime>();
            var outerLifetime = testHost.WebHostServices.GetRequiredService<IHostApplicationLifetime>();

            var outerStopping = new TaskCompletionSource();
            using var registration = outerLifetime.ApplicationStopping.Register(() => outerStopping.TrySetResult());

            innerLifetime.StopApplication();

            await outerStopping.Task.TestWaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(outerLifetime.ApplicationStopping.IsCancellationRequested);
        }
    }
}

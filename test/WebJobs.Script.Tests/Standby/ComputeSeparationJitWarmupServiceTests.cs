// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Standby;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Standby
{
    public class ComputeSeparationJitWarmupServiceTests
    {
        [Fact]
        public async Task StartAsync_DoesNotBlock_AndRunsJitTraceWarmerInBackground()
        {
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(new TestLoggerProvider());

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { EnvironmentSettingNames.FunctionsWorkerExternalEnabled, "true" }
                })
                .Build();

            var warmer = new JitTraceWarmer(new TestEnvironment(), configuration, loggerFactory.CreateLogger<JitTraceWarmer>());
            var service = new ComputeSeparationJitWarmupService(warmer, loggerFactory.CreateLogger<ComputeSeparationJitWarmupService>());

            // StartAsync must return promptly without waiting for PreJIT to complete.
            var startTask = service.StartAsync(CancellationToken.None);
            Assert.True(startTask.IsCompletedSuccessfully);

            // Eventually the background warmer should run.
            await TestHelpers.Await(() => warmer.HasRun, timeout: 10_000, pollingInterval: 50);

            await service.StopAsync(CancellationToken.None);
        }
    }
}

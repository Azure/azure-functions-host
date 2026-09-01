// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
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
            await using var testHost = new TestFunctionHost(_testScriptPath, _testLogPath);

            var innerLifetime = testHost.JobHostServices.GetRequiredService<IScriptApplicationLifetime>();
            var outerLifetime = testHost.WebHostServices.GetRequiredService<IHostApplicationLifetime>();

            var outerStopping = new TaskCompletionSource();
            using var registration = outerLifetime.ApplicationStopping.Register(() => outerStopping.TrySetResult());

            innerLifetime.StopApplication();

            await outerStopping.Task.TestWaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(outerLifetime.ApplicationStopping.IsCancellationRequested);
        }

        [Fact]
        public async Task RunHostAsync_StopApplicationDuringOuterHostStartup_StopsHostedServices()
        {
            var startedService = new TrackingHostedService();
            using IHost host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IScriptApplicationLifetime, ScriptApplicationLifetime>();
                    services.AddSingleton<IHostedService>(startedService);
                    services.AddSingleton<WorkerStartupTimeoutHostedService>();
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WorkerStartupTimeoutHostedService>());
                })
                .Build();
            var timeoutService = host.Services.GetRequiredService<WorkerStartupTimeoutHostedService>();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Program.RunHostAsync(host));

            Assert.True(startedService.Started);
            Assert.True(startedService.Stopped);
            Assert.True(timeoutService.Stopped);
        }

        [Fact]
        public async Task RunHostAsync_StopApplicationDuringOuterHostStartupWithNonCancellationException_StopsHostedServices()
        {
            var startedService = new TrackingHostedService();
            using IHost host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IScriptApplicationLifetime, ScriptApplicationLifetime>();
                    services.AddSingleton<IHostedService>(startedService);
                    services.AddSingleton<StopApplicationAndThrowHostedService>();
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<StopApplicationAndThrowHostedService>());
                })
                .Build();
            var startupService = host.Services.GetRequiredService<StopApplicationAndThrowHostedService>();

            InvalidOperationException exception =
                await Assert.ThrowsAsync<InvalidOperationException>(() => Program.RunHostAsync(host));

            Assert.Same(startupService.StartupException, exception);
            Assert.True(startedService.Started);
            Assert.True(startedService.Stopped);
            Assert.True(startupService.Stopped);
        }

        [Fact]
        public async Task RunHostAsync_StopApplicationDuringOuterHostStartupWhenStopFails_PreservesBothExceptions()
        {
            var stopFailureService = new ThrowingStopHostedService();
            using IHost host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IScriptApplicationLifetime, ScriptApplicationLifetime>();
                    services.AddSingleton<IHostedService>(stopFailureService);
                    services.AddSingleton<WorkerStartupTimeoutHostedService>();
                    services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WorkerStartupTimeoutHostedService>());
                })
                .Build();

            AggregateException exception =
                await Assert.ThrowsAsync<AggregateException>(() => Program.RunHostAsync(host));

            Assert.StartsWith("Host startup failed and shutdown also failed.", exception.Message);
            Assert.Collection(
                exception.InnerExceptions,
                startupException => Assert.IsAssignableFrom<OperationCanceledException>(startupException),
                shutdownException => Assert.Same(stopFailureService.StopException, shutdownException));
        }

        [Fact]
        public async Task RunHostAsync_StopApplicationAfterOuterHostStartup_StopsHostedServices()
        {
            var service = new TrackingHostedService();
            using IHost host = new HostBuilder()
                .ConfigureServices(services => services.AddSingleton<IHostedService>(service))
                .Build();
            IHostApplicationLifetime applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var applicationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration =
                applicationLifetime.ApplicationStarted.Register(() => applicationStarted.TrySetResult());

            Task runTask = Program.RunHostAsync(host);
            await applicationStarted.Task.TestWaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(runTask.IsCompleted);

            applicationLifetime.StopApplication();

            await runTask.TestWaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(service.Started);
            Assert.True(service.Stopped);
        }

        private sealed class TrackingHostedService : IHostedService
        {
            public bool Started { get; private set; }

            public bool Stopped { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                Started = true;
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Stopped = true;
                return Task.CompletedTask;
            }
        }

        private sealed class StopApplicationAndThrowHostedService : IHostedService
        {
            private readonly IScriptApplicationLifetime _applicationLifetime;

            public StopApplicationAndThrowHostedService(IScriptApplicationLifetime applicationLifetime)
            {
                _applicationLifetime = applicationLifetime;
            }

            public InvalidOperationException StartupException { get; } = new("Startup failed.");

            public bool Stopped { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _applicationLifetime.StopApplication();
                throw StartupException;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Stopped = true;
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingStopHostedService : IHostedService
        {
            public InvalidOperationException StopException { get; } = new("Shutdown failed.");

            public Task StartAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                throw StopException;
            }
        }

        private sealed class WorkerStartupTimeoutHostedService : IHostedService
        {
            private readonly IScriptApplicationLifetime _applicationLifetime;

            public WorkerStartupTimeoutHostedService(IScriptApplicationLifetime applicationLifetime)
            {
                _applicationLifetime = applicationLifetime;
            }

            public bool Stopped { get; private set; }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                _applicationLifetime.StopApplication();
                return Task.Delay(Timeout.Infinite, cancellationToken);
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                Stopped = true;
                return Task.CompletedTask;
            }
        }
    }
}

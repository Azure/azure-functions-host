// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebJobsScriptHostServiceTests
    {
        private WebJobsScriptHostService _hostService;
        private ScriptApplicationHostOptionsMonitor _monitor;
        private TestLoggerProvider _webHostLoggerProvider = new TestLoggerProvider();
        private TestLoggerProvider _jobHostLoggerProvider = new TestLoggerProvider();
        private ILoggerFactory _loggerFactory;
        private Mock<IScriptWebHostEnvironment> _mockScriptWebHostEnvironment;
        private Mock<IEnvironment> _mockEnvironment;
        private OptionsWrapper<HostHealthMonitorOptions> _healthMonitorOptions;
        private HostPerformanceManager _hostPerformanceManager;
        private Mock<IHost> _host;

        public WebJobsScriptHostServiceTests()
        {
            var options = new ScriptApplicationHostOptions
            {
                ScriptPath = @"c:\tests",
                LogPath = @"c:\tests\logs",
            };
            _monitor = new ScriptApplicationHostOptionsMonitor(options);
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_webHostLoggerProvider);

            _host = CreateMockHost();

            _mockScriptWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            _mockEnvironment = new Mock<IEnvironment>();
            _healthMonitorOptions = new OptionsWrapper<HostHealthMonitorOptions>(new HostHealthMonitorOptions());
            var serviceProviderMock = new Mock<IServiceProvider>(MockBehavior.Strict);
            _hostPerformanceManager = new HostPerformanceManager(_mockEnvironment.Object, _healthMonitorOptions, serviceProviderMock.Object);
        }

        private Mock<IHost> CreateMockHost(SemaphoreSlim disposedSemaphore = null)
        {
            // The tests can pull the logger from a specific host if they need to.
            var services = new ServiceCollection()
               .AddLogging(l =>
               {
                   l.Services.AddSingleton<ILoggerProvider, TestLoggerProvider>();
                   l.AddFilter(_ => true);
               })
               .BuildServiceProvider();

            var host = new Mock<IHost>();

            host.Setup(h => h.Services)
                .Returns(services);

            host.Setup(h => h.Dispose())
                .Callback(() =>
                {
                    services.Dispose();
                    disposedSemaphore?.Release();
                });

            return host;
        }

        [Fact]
        public async Task HostInitialization_OnInitializationException_MaintainsErrorInformation()
        {
            // When an exception is thrown, we'll create a new host. Make sure
            // we don't return the same one (with disposed services) the second time.
            var metricsLogger = new TestMetricsLogger();
            var hostA = CreateMockHost();
            hostA.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new HostInitializationException("boom"));

            var hostB = CreateMockHost();
            hostB.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.SetupSequence(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(hostA.Object)
                .Returns(hostB.Object);

            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, NullLoggerFactory.Instance,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            await _hostService.StartAsync(CancellationToken.None);
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));
            Assert.Equal(ScriptHostState.Error, _hostService.State);
            Assert.IsType<HostInitializationException>(_hostService.LastError);
        }

        [Fact]
        public async Task HostRestart_Specialization_Succeeds()
        {
            var metricsLogger = new TestMetricsLogger();
            _host.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.Setup(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(_host.Object);

            _webHostLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_webHostLoggerProvider);

            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, _loggerFactory,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            await _hostService.StartAsync(CancellationToken.None);
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));

            Thread restartHostThread = new Thread(new ThreadStart(RestartHost));
            Thread specializeHostThread = new Thread(new ThreadStart(SpecializeHost));
            restartHostThread.Start();
            specializeHostThread.Start();
            restartHostThread.Join();
            specializeHostThread.Join();

            var logMessages = _webHostLoggerProvider.GetAllLogMessages().Where(m => m.FormattedMessage.Contains("Restarting host."));
            Assert.Equal(2, logMessages.Count());
        }

        [Fact]
        public async Task HostRestart_DuringInitializationWithError_Recovers()
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
            await semaphore.WaitAsync();
            bool waitingInHostBuilder = false;
            var metricsLogger = new TestMetricsLogger();

            // Have the first host start, but pause. We'll issue a restart, then let
            // this first host throw an exception.
            var hostA = CreateMockHost();
            hostA
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    waitingInHostBuilder = true;
                    await semaphore.WaitAsync();
                    throw new InvalidOperationException("Something happened at startup!");
                });

            var hostB = CreateMockHost();
            hostB
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.SetupSequence(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(hostA.Object)
                .Returns(hostB.Object);

            _webHostLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_webHostLoggerProvider);

            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, _loggerFactory,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            TestLoggerProvider hostALogger = hostA.Object.GetTestLoggerProvider();
            TestLoggerProvider hostBLogger = hostB.Object.GetTestLoggerProvider();

            Task initialStart = _hostService.StartAsync(CancellationToken.None);

            Thread restartHostThread = new Thread(new ThreadStart(RestartHost));
            restartHostThread.Start();
            await TestHelpers.Await(() => waitingInHostBuilder);

            // Now let the first host continue. It will throw, which should be correctly handled.
            semaphore.Release();

            await TestHelpers.Await(() => _hostService.State == ScriptHostState.Running);

            restartHostThread.Join();

            await initialStart;
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));

            // Make sure the error was logged to the correct logger
            Assert.Contains(hostALogger.GetAllLogMessages(), m => m.FormattedMessage != null && m.FormattedMessage.Contains("A host error has occurred"));
            Assert.DoesNotContain(hostBLogger.GetAllLogMessages(), m => m.FormattedMessage != null && m.FormattedMessage.Contains("A host error has occurred"));

            // Make sure we orphaned the correct host when the exception was thrown.
            hostA.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(1));
            hostA.Verify(m => m.Dispose(), Times.Exactly(1));

            // We should not be calling Orphan on the good host
            hostB.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            hostB.Verify(m => m.Dispose(), Times.Never);

            // The "late" exception from the first host shouldn't bring things down
            Assert.Equal(ScriptHostState.Running, _hostService.State);
        }

        [Fact]
        public async Task HostRestart_DuringInitialization_Cancels()
        {
            SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
            await semaphore.WaitAsync();
            bool waitingInHostBuilder = false;
            var metricsLogger = new TestMetricsLogger();

            // Have the first host start, but pause. We'll issue a restart, then
            // let this first host continue and see the cancelation token has fired.
            var hostA = CreateMockHost();
            hostA
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns<CancellationToken>(async ct =>
                {
                    waitingInHostBuilder = true;
                    await semaphore.WaitAsync();
                    ct.ThrowIfCancellationRequested();
                });

            var hostB = CreateMockHost();
            hostB
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() => Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.SetupSequence(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(hostA.Object)
                .Returns(hostB.Object);

            _webHostLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_webHostLoggerProvider);

            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, _loggerFactory,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            TestLoggerProvider hostALogger = hostA.Object.GetTestLoggerProvider();

            Task initialStart = _hostService.StartAsync(CancellationToken.None);

            Thread restartHostThread = new Thread(new ThreadStart(RestartHost));
            restartHostThread.Start();
            await TestHelpers.Await(() => waitingInHostBuilder);
            await TestHelpers.Await(() => _webHostLoggerProvider.GetLog().Contains("Restart requested."));

            // Now let the first host continue. It will see that startup is canceled.
            semaphore.Release();

            await TestHelpers.Await(() => _hostService.State == ScriptHostState.Running);

            restartHostThread.Join();

            await initialStart;
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));

            // Make sure the error was logged to the correct logger
            Assert.Contains(hostALogger.GetAllLogMessages(), m => m.FormattedMessage != null && m.FormattedMessage.StartsWith("Host startup operation '") && m.FormattedMessage.EndsWith("' was canceled."));

            // Make sure we orphaned the correct host
            hostA.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Exactly(1));
            hostA.Verify(m => m.Dispose(), Times.Exactly(1));

            // We should not be calling Orphan on the good host
            hostB.Verify(m => m.StopAsync(It.IsAny<CancellationToken>()), Times.Never);
            hostB.Verify(m => m.Dispose(), Times.Never);

            Assert.Equal(ScriptHostState.Running, _hostService.State);
        }

        [Fact]
        public async Task DisposedHost_ServicesNotExposed()
        {
            SemaphoreSlim blockingSemaphore = new SemaphoreSlim(0, 1);
            SemaphoreSlim disposedSemaphore = new SemaphoreSlim(0, 1);
            var metricsLogger = new TestMetricsLogger();

            // Have the first host throw upon starting. Then pause while building the second
            // host. When accessing Services then, they should be null, rather than disposed.
            var hostA = CreateMockHost(disposedSemaphore);
            hostA
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(() =>
                {
                    throw new InvalidOperationException("Something happened at startup!");
                });

            var hostB = CreateMockHost();
            hostB
                .Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await blockingSemaphore.WaitAsync();
                });

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.SetupSequence(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(hostA.Object)
                .Returns(hostB.Object);

            _hostService = new WebJobsScriptHostService(
               _monitor, hostBuilder.Object, NullLoggerFactory.Instance,
               _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            Task startTask = _hostService.StartAsync(CancellationToken.None);

            await disposedSemaphore.WaitAsync();

            Assert.Null(_hostService.Services);

            blockingSemaphore.Release();

            await startTask;
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));
        }

        [Fact]
        public async Task HostRestart_BeforeStart_WaitsForStartToContinue()
        {
            _host = CreateMockHost();
            var metricsLogger = new TestMetricsLogger();
            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.SetupSequence(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(_host.Object)
                .Returns(_host.Object);

            _webHostLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_webHostLoggerProvider);

            _hostService = new WebJobsScriptHostService(
               _monitor, hostBuilder.Object, _loggerFactory,
               _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions, metricsLogger, new Mock<IApplicationLifetime>().Object);

            // Simulate a call to specialize coming from the PlaceholderSpecializationMiddleware. This
            // can happen before we ever start the service, which could create invalid state.
            Task restartTask = _hostService.RestartHostAsync(CancellationToken.None);

            await _hostService.StartAsync(CancellationToken.None);
            await restartTask;

            // Ensure metrics are added
            Assert.True(AreRequiredMetricsGenerated(metricsLogger));
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerRestartService));
            Assert.True(metricsLogger.EventsEnded.Contains(MetricEventNames.ScriptHostManagerRestartService));
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerBuildScriptHost));
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerBuildScriptHost));
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerStartScriptHost));
            Assert.True(metricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerStartScriptHost));

            var messages = _webHostLoggerProvider.GetAllLogMessages();

            // The CancellationToken is canceled quick enough that we never build the initial host, so the
            // only one we start is the restarted/specialized one.
            Assert.NotNull(messages.Single(p => p.EventId.Id == 513)); // "Building" message
            Assert.NotNull(messages.Single(p => p.EventId.Id == 514)); // "StartupWasCanceled" message
            Assert.NotNull(messages.Single(p => p.EventId.Id == 520)); // "RestartBeforeStart" message
            _host.Verify(p => p.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        public void RestartHost()
        {
            _hostService.RestartHostAsync(CancellationToken.None).Wait();
        }

        public void SpecializeHost()
        {
            var mockScriptWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            Mock<IConfigurationRoot> mockConfiguration = new Mock<IConfigurationRoot>();
            var mockEnvironment = new Mock<IEnvironment>();
            Mock<IWebHostRpcWorkerChannelManager> mockLanguageWorkerChannelManager = new Mock<IWebHostRpcWorkerChannelManager>();
            ILogger<StandbyManager> testLogger = new Logger<StandbyManager>(_loggerFactory);
            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var hostNameProvider = new HostNameProvider(mockEnvironment.Object);
            var mockApplicationLifetime = new Mock<IApplicationLifetime>(MockBehavior.Strict);
            var manager = new StandbyManager(_hostService, mockLanguageWorkerChannelManager.Object, mockConfiguration.Object, mockScriptWebHostEnvironment.Object, mockEnvironment.Object, _monitor, testLogger, hostNameProvider, mockApplicationLifetime.Object, new TestMetricsLogger());
            manager.SpecializeHostAsync().Wait();
        }

        private bool AreRequiredMetricsGenerated(TestMetricsLogger testMetricsLogger)
        {
            return testMetricsLogger.EventsBegan.Contains(MetricEventNames.ScriptHostManagerStartService) && testMetricsLogger.EventsEnded.Contains(MetricEventNames.ScriptHostManagerStartService);
        }

        private class ThrowThenPauseScriptHostBuilder : IScriptHostBuilder
        {
            private readonly Action _pause;
            private int _count = 0;

            public ThrowThenPauseScriptHostBuilder(Action pause)
            {
                _pause = pause;
            }

            public IHost BuildHost(bool skipHostStartup, bool skipHostConfigurationParsing)
            {
                if (_count == 0)
                {
                    _count++;
                    var services = new ServiceCollection();
                    var rootServiceProvider = new WebHostServiceProvider(services);
                    var mockDepValidator = new Mock<IDependencyValidator>();

                    var host = new HostBuilder()
                        .UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootServiceProvider, mockDepValidator.Object))
                        .Build();

                    throw new InvalidOperationException("boom!");
                }
                else if (_count == 1)
                {
                    _count++;
                    _pause();
                }
                else
                {
                    throw new InvalidOperationException("We should never get here.");
                }

                return null;
            }
        }
    }
}

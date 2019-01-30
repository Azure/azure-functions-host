// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebJobsScriptHostServiceTests
    {
        private WebJobsScriptHostService _hostService;
        private ScriptApplicationHostOptionsMonitor _monitor;
        private TestLoggerProvider _testLoggerProvider;
        private ILoggerFactory _loggerFactory;
        private Mock<IServiceProvider> _mockRootServiceProvider;
        private Mock<IServiceScopeFactory> _mockRootScopeFactory;
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
            var services = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider();
            _host = new Mock<IHost>();
            _host.Setup(h => h.Services)
                .Returns(services);

            _mockRootServiceProvider = new Mock<IServiceProvider>();
            _mockRootScopeFactory = new Mock<IServiceScopeFactory>();
            _mockScriptWebHostEnvironment = new Mock<IScriptWebHostEnvironment>();
            _mockEnvironment = new Mock<IEnvironment>();
            _healthMonitorOptions = new OptionsWrapper<HostHealthMonitorOptions>(new HostHealthMonitorOptions());
            _hostPerformanceManager = new HostPerformanceManager(_mockEnvironment.Object, _healthMonitorOptions);
        }

        [Fact]
        public async Task HostInitialization_OnInitializationException_MaintainsErrorInformation()
        {
            _host.SetupSequence(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Throws(new HostInitializationException("boom"))
                .Returns(Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.Setup(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(_host.Object);
            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, NullLoggerFactory.Instance, _mockRootServiceProvider.Object, _mockRootScopeFactory.Object,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions);

            await _hostService.StartAsync(CancellationToken.None);

            Assert.Equal(ScriptHostState.Error, _hostService.State);
            Assert.IsType<HostInitializationException>(_hostService.LastError);
        }

        [Fact]
        public async Task HostRestart_Specialization_Succeeds()
        {
            _host.Setup(h => h.StartAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var hostBuilder = new Mock<IScriptHostBuilder>();
            hostBuilder.Setup(b => b.BuildHost(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns(_host.Object);

            _testLoggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_testLoggerProvider);

            _hostService = new WebJobsScriptHostService(
                _monitor, hostBuilder.Object, _loggerFactory, _mockRootServiceProvider.Object, _mockRootScopeFactory.Object,
                _mockScriptWebHostEnvironment.Object, _mockEnvironment.Object, _hostPerformanceManager, _healthMonitorOptions);

            await _hostService.StartAsync(CancellationToken.None);

            Thread restartHostThread = new Thread(new ThreadStart(RestartHost));
            Thread specializeHostThread = new Thread(new ThreadStart(SpecializeHost));
            restartHostThread.Start();
            specializeHostThread.Start();
            restartHostThread.Join();
            specializeHostThread.Join();

            var logMessages = _testLoggerProvider.GetAllLogMessages().Where(m => m.FormattedMessage.Contains("Restarting host."));
            Assert.Equal(2, logMessages.Count());
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
            Mock<ILanguageWorkerChannelManager> mockLanguageWorkerChannelManager = new Mock<ILanguageWorkerChannelManager>();
            ILogger<StandbyManager> testLogger = new Logger<StandbyManager>(_loggerFactory);
            var manager = new StandbyManager(_hostService, mockLanguageWorkerChannelManager.Object, mockConfiguration.Object, mockScriptWebHostEnvironment.Object, mockEnvironment.Object, _monitor, testLogger);
            manager.SpecializeHostAsync().Wait();
        }
    }
}

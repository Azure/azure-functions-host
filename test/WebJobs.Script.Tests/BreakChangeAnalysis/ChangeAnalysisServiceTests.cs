// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.ChangeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.BreakChangeAnalysis
{
    public class ChangeAnalysisServiceTests
    {
        private readonly IConfigurationRoot _configuration;
        private readonly IHostIdProvider _hostIdProvider;

        public ChangeAnalysisServiceTests()
        {
            _configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .AddTestSettings()
                .Build();

            var hostIdProviderMock = new Mock<IHostIdProvider>(MockBehavior.Strict);
            hostIdProviderMock.Setup(p => p.GetHostIdAsync(CancellationToken.None))
                .ReturnsAsync($"testhost123{Guid.NewGuid().ToString().Replace("-", string.Empty)}");

            _hostIdProvider = hostIdProviderMock.Object;
        }

        [Fact]
        public async Task TryLogBreakingChangeReportAsync_WithRecentState_SkipsAnalysis()
        {
            DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddDays(-5);

            (ChangeAnalysisService service, TestLoggerProvider loggerProvider) = CreateService(timestamp);

            await service.TryLogBreakingChangeReportAsync(CancellationToken.None);

            Assert.True(loggerProvider.GetAllLogMessages().Any(m => string.Equals(m.FormattedMessage, "Skipping breaking change analysis.", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task TryLogBreakingChangeReportAsync_WithStaleState_PerformsAnalysis()
        {
            var setup = new Action<Mock<IChangeAnalysisStateProvider>>(p =>
            {
                p.Setup(m => m.SetTimestampAsync(It.IsAny<DateTimeOffset>(), null, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            });

            DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddDays(-7.1);

            (ChangeAnalysisService service, TestLoggerProvider loggerProvider) = CreateService(timestamp, setup);

            await service.TryLogBreakingChangeReportAsync(CancellationToken.None);

            Assert.True(loggerProvider.GetAllLogMessages().Any(m => string.Equals(m.FormattedMessage, "Breaking change analysis operation completed.", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task TryLogBreakingChangeReportAsync_WithCancelledToken_HandlesException()
        {
            (ChangeAnalysisService service, TestLoggerProvider loggerProvider) = CreateService(DateTimeOffset.UtcNow);

            var cts = new CancellationTokenSource();
            cts.Cancel();

            await service.TryLogBreakingChangeReportAsync(cts.Token);

            Assert.True(loggerProvider.GetAllLogMessages().Any(m => string.Equals(m.FormattedMessage,
                "Breaking change analysis operation cancelled.", StringComparison.Ordinal)));
        }

        private (ChangeAnalysisService, TestLoggerProvider) CreateService(DateTimeOffset lastAnalysisTimestamp,
            Action<Mock<IChangeAnalysisStateProvider>> changeStateProviderSetup = null)
        {
            var primaryHostMock = new Mock<IPrimaryHostStateProvider>();
            primaryHostMock.Setup(p => p.IsPrimary).Returns(true);

            var changeAnalysisStateProvider = new Mock<IChangeAnalysisStateProvider>(MockBehavior.Strict);
            changeAnalysisStateProvider.Setup(p => p.GetCurrentAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChangeAnalysisState(lastAnalysisTimestamp, null));

            changeStateProviderSetup?.Invoke(changeAnalysisStateProvider);

            var loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);

            var changeAnalysisService = new ChangeAnalysisService(loggerFactory.CreateLogger<ChangeAnalysisService>(),
                                                                  new TestEnvironment(),
                                                                  changeAnalysisStateProvider.Object,
                                                                  primaryHostMock.Object);

            return (changeAnalysisService, loggerProvider);
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class DiagnosticEventLoggerTests
    {
        [Fact]
        public void DiagnosticEventLogger_OnlyLogsMessages_WithRequiredProperties()
        {
            var repository = new TestDiagnosticEventRepository();
            var repositoryFactory = new TestDiagnosticEventRepositoryFactory(repository);
            var environment = new TestEnvironment();
            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(new StandbyOptions { InStandbyMode = false });
            using (var provider = new DiagnosticEventLoggerProvider(repositoryFactory, environment, standbyOptions))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");

                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occurred", "https://fwlink", null);

                logger.LogInformation("Error code: {MS_errorCode}, Error Message: {message}, HelpLink: {MS_HelpLink}", "Error123", "Unknown Error", "http://helpLink");
            }

            Assert.Equal(repository.Events.Count, 1);
            Assert.Equal(repository.Events.First().ErrorCode, "FN123");
        }

        [Fact]
        public void DiagnosticEventLogger_OnlyLogsMessages_WhenSpecialized()
        {
            var repository = new TestDiagnosticEventRepository();
            var repositoryFactory = new TestDiagnosticEventRepositoryFactory(repository);
            var environment = new TestEnvironment();
            var options = new StandbyOptions { InStandbyMode = true };
            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(options);
            using (var provider = new DiagnosticEventLoggerProvider(repositoryFactory, environment, standbyOptions))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");

                // this one should not appear
                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN999", "Actionable event occurred", "https://fwlink", null);

                options.InStandbyMode = false;
                standbyOptions.InvokeChanged();

                // now that specialized, logger works
                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occurred", "https://fwlink", null);
            }

            Assert.Equal(1, repository.Events.Count);
            Assert.Equal("FN123", repository.Events.First().ErrorCode);
        }

        [Fact]
        public void DiagnosticEventLogger_DoesNotLog_IfFeatureFlagDisabled()
        {
            var repository = new TestDiagnosticEventRepository();
            var repositoryFactory = new TestDiagnosticEventRepositoryFactory(repository);
            var environment = new TestEnvironment();
            var options = new StandbyOptions { InStandbyMode = true };
            var standbyOptions = new TestOptionsMonitor<StandbyOptions>(options);
            using (var provider = new DiagnosticEventLoggerProvider(repositoryFactory, environment, standbyOptions))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");

                // this one will not appear because of standby mode
                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occurred", "https://fwlink", null);

                // we cache the _isEnabled, so need to force it to re-evaluate
                // this will happen during specialization
                options.InStandbyMode = false;
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, ScriptConstants.FeatureFlagDisableDiagnosticEventLogging);
                standbyOptions.InvokeChanged();

                // this one will still not appear due to the feature flag
                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN999", "Actionable event occurred", "https://fwlink", null);
            }

            Assert.Empty(repository.Events);
        }

        public class TestDiagnosticEventRepositoryFactory : IDiagnosticEventRepositoryFactory
        {
            private IDiagnosticEventRepository _repository;

            public TestDiagnosticEventRepositoryFactory(IDiagnosticEventRepository repository)
            {
                _repository = repository;
            }

            public IDiagnosticEventRepository Create()
            {
                return _repository;
            }
        }

        public class TestDiagnosticEventRepository : IDiagnosticEventRepository
        {
            private readonly List<DiagnosticEvent> _events;

            public TestDiagnosticEventRepository()
            {
                _events = new List<DiagnosticEvent>();
            }

            public List<DiagnosticEvent> Events => _events;

            public void WriteDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
            {
                _events.Add(new DiagnosticEvent("hostid", timestamp)
                {
                    ErrorCode = errorCode,
                    LogLevel = level,
                    Message = message,
                    HelpLink = helpLink,
                    Details = exception?.Message
                });
            }

            public void FlushLogs()
            {
                Events.Clear();
            }
        }
    }
}

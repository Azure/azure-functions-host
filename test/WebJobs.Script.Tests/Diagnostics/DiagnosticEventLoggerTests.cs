// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class DiagnosticEventLoggerTests
    {
        [Fact]
        public void DiagnosticEventLogger_OnlylogsMessages_WithRequiredProperties()
        {
            var repository = new TestDiagnosticEventRepository();
            var repositoryFactory = new TestDiagnosticEventRepositoryFactory(repository);
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            using (var provider = new DiagnosticEventLoggerProvider(repositoryFactory, environment))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");

                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occurred", "https://fwlink", null);

                logger.LogInformation("Error code: {MS_errorCode}, Error Message: {message}, HelpLink: {MS_HelpLink}", "Erro123", "Unknown Error", "http://helpLink");
            }

            Assert.Equal(repository.Events.Count, 1);
            Assert.Equal(repository.Events.First().ErrorCode, "FN123");
        }

        [Fact]
        public void DiagnosticEventLogger_OnlylogsMessages_WhenSpecialized()
        {
            var repository = new TestDiagnosticEventRepository();
            var repositoryFactory = new TestDiagnosticEventRepositoryFactory(repository);
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            using (var provider = new DiagnosticEventLoggerProvider(repositoryFactory, environment))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");
                logger.LogInformation("Error code: {MS_errorCode}, Error Message: {message}, HelpLink: {MS_HelpLink}", "Erro123", "Unknown Error", "http://helpLink");

                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occurred", "https://fwlink", null);
            }

            Assert.Equal(1, repository.Events.Count);
            Assert.Equal("FN123", repository.Events.First().ErrorCode);
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
                    LastTimeStamp = timestamp,
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

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
    public class DiagnosticEventLoggerProviderTests
    {
        [Fact]
        public void DiagnosticEventLogger_OnlylogsMessages_WithRequiredProperties()
        {
            var repository = new TestDiagnosticEventRepository();
            using (var provider = new DiagnosticEventLoggerProvider(repository))
            {
                var logger = provider.CreateLogger("MS_DiagnosticEvents");

                logger.LogDiagnosticEvent(LogLevel.Error, 123, "FN123", "Actionable event occured", "https://fwlink", null);

                logger.LogInformation("Error code: {errorCode}, Error Message: {message}, HelpLink: {helpLink}", "Erro123", "Unknown Error", "http://helpLink");
            }

            Assert.Equal(repository.Events.Count, 1);
            Assert.Equal(repository.Events.First().ErrorCode, "FN123");
        }

        public class TestDiagnosticEventRepository : IDiagnosticEventRepository
        {
            private readonly List<DiagnosticEvent> _events;

            public TestDiagnosticEventRepository()
            {
                _events = new List<DiagnosticEvent>();
            }

            public List<DiagnosticEvent> Events => _events;

            public void AddDiagnosticEvent(DateTime timestamp, string errorCode, LogLevel level, string message, string helpLink, Exception exception)
            {
                _events.Add(new DiagnosticEvent()
                {
                    LastTimeStamp = timestamp,
                    ErrorCode = errorCode,
                    Level = level,
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

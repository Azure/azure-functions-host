// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System;
using Xunit;
using Microsoft.WebJobs.Script.Tests;
using Microsoft.Azure.AppService.Proxy.Common.Extensions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Diagnostics
{
    public class DiagnosticEventTestUtils
    {
        public static void ValidateThatTheExpectedDiagnosticEventIsPresent(
            TestLoggerProvider loggerProvider,
            string expectedMessage,
            LogLevel logLevel,
            string helpLink,
            string errorCode
        )
        {
            LogMessage actualEvent = null;

            // Find the expected diagnostic event
            foreach (var message in loggerProvider.GetAllLogMessages())
            {
                if (message.FormattedMessage.IndexOf(expectedMessage, StringComparison.OrdinalIgnoreCase) > -1 &&
                    message.Level == logLevel &&
                    message.State is Dictionary<string, object> dictionary &&
                    dictionary.ContainsKey("MS_HelpLink") && dictionary.ContainsKey("MS_ErrorCode") &&
                    dictionary.GetValueOrDefault("MS_HelpLink").ToString().Equals(helpLink, StringComparison.OrdinalIgnoreCase) &&
                    dictionary.GetValueOrDefault("MS_ErrorCode").ToString().Equals(errorCode, StringComparison.OrdinalIgnoreCase))
                {
                    actualEvent = message;
                    break;
                }
            }

            // Make sure that the expected event was found
            Assert.NotNull(actualEvent);
        }
    }
}

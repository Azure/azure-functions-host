// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class MockNullLogerFactory
    {
        public static ILoggerFactory CreateLoggerFactory()
        {
            var loggerFactory = Substitute.For<ILoggerFactory>();
            var logger = CreateLogger();
            loggerFactory.CreateLogger(Arg.Any<string>()).Returns(logger);
            return loggerFactory;
        }

        public static ILogger CreateLogger()
        {
            var logger = Substitute.For<ILogger>();
            logger.Log(Arg.Any<LogLevel>(),
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception>(),
                Arg.Any<Func<object, Exception, string>>());
            return logger;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class MockNullLogerFactory
    {
        public static ILoggerFactory CreateLoggerFactory()
        {
            var loggerFactory = new Mock<ILoggerFactory>();
            var logger = CreateLogger();
            loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger);
            return loggerFactory.Object;
        }

        public static ILogger CreateLogger()
        {
            var logger = new Mock<ILogger>();
            logger.Setup(l => l.Log(It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<object>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<object, Exception, string>>()));
            return logger.Object;
        }
    }
}

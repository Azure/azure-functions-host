// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public static class LoggerFactoryExtensions
    {
        public static ILoggerFactory AddTestLoggerProvider(this ILoggerFactory factory, out TestLoggerProvider loggerProvider)
        {
            loggerProvider = new TestLoggerProvider();
            factory.AddProvider(loggerProvider);
            return factory;
        }

        public static ILoggerFactory AddTestOutputHelperLoggerProvider(this ILoggerFactory factory, ITestOutputHelper outputHelper)
        {
            factory.AddProvider(new TestOutputHelperLoggerProvider(outputHelper));
            return factory;
        }
    }
}

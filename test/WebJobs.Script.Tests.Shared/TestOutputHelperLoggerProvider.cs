// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestOutputHelperLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper _outputHelper;

        public TestOutputHelperLoggerProvider(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestOutputHelperLogger(_outputHelper);
        }

        public void Dispose()
        {
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.UnitTests.Executors
{
    public class FunctionResultTests
    {
        [Fact]
        public void Constructor_Boolean()
        {
            FunctionResult result = new FunctionResult(true);
            Assert.True(result.Succeeded);
            Assert.Null(result.Exception);

            result = new FunctionResult(false);
            Assert.False(result.Succeeded);
            Assert.Null(result.Exception);
        }

        [Fact]
        public void Constructor_Exception()
        {
            Exception exception = new Exception("Kaboom!");
            FunctionResult result = new FunctionResult(exception);
            Assert.False(result.Succeeded);
            Assert.Same(exception, result.Exception);
        }

        [Fact]
        public void Constructor_BooleanAndException()
        {
            Exception exception = new Exception("Kaboom!");
            FunctionResult result = new FunctionResult(true, exception);
            Assert.True(result.Succeeded);
            Assert.Same(exception, result.Exception);

            result = new FunctionResult(false, exception);
            Assert.False(result.Succeeded);
            Assert.Same(exception, result.Exception);
        }
    }
}

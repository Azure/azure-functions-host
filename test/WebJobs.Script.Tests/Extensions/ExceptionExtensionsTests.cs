// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Extensions
{
    public class ExceptionExtensionsTests
    {
        [Fact]
        public void GetExceptionDetails_ReturnsExpectedResult()
        {
            Exception innerException = new InvalidOperationException("Some inner exception");
            Exception outerException = new Exception("some outer exception", innerException);
            Exception fullException;

            try
            {
                throw outerException;
            }
            catch (Exception e)
            {
                fullException = e;  // Outer exception will have stack trace whereas the inner exception's stack trace will be null
            }

            (string exceptionType, string exceptionMessage, string exceptionDetails) = fullException.GetExceptionDetails();

            Assert.Equal("System.InvalidOperationException", exceptionType);
            Assert.Equal("Some inner exception", exceptionMessage);
            Assert.Contains("System.Exception : some outer exception ---> System.InvalidOperationException : Some inner exception", exceptionDetails);
            Assert.Contains("End of inner exception", exceptionDetails);
            Assert.Contains("at Microsoft.Azure.WebJobs.Script.Tests.Extensions.ExceptionExtensionsTests.GetExceptionDetails_ReturnsExpectedResult()", exceptionDetails);
            Assert.Contains("ExceptionExtensionsTests.cs : 20", exceptionDetails);
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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
            Assert.Contains("ExceptionExtensionsTests.cs", exceptionDetails);
        }

        [Fact]
        public void GetExceptionDetails_Rpc()
        {
            string rpcMessage = "rpcMessage";
            Exception innerException = new RpcException("result", rpcMessage, "stack");
            Exception outerException = new FunctionInvocationException("message", innerException);
            Exception fullException;

            try
            {
                throw outerException;
            }
            catch (Exception e)
            {
                fullException = e;
            }

            (string exceptionType, string exceptionMessage, string exceptionDetails, string formattedText) = fullException.GetSanitizedExceptionDetails("safe text");

            Assert.Equal("Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException", exceptionType);
            Assert.DoesNotContain(rpcMessage, exceptionMessage);
            Assert.DoesNotContain(rpcMessage, exceptionDetails);
            Assert.Contains("safe text", formattedText);
        }

        [Fact]
        public void GetExceptionDetails_Rpc_Empty()
        {
            Exception innerException = new RpcException(string.Empty, string.Empty, string.Empty);
            Exception outerException = new FunctionInvocationException(string.Empty, innerException);
            Exception fullException;

            try
            {
                throw outerException;
            }
            catch (Exception e)
            {
                fullException = e;
            }

            (string exceptionType, string exceptionMessage, _, string formattedText) = fullException.GetSanitizedExceptionDetails("safe text");

            Assert.Equal("Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException", exceptionType);
            Assert.Equal("Result: \nType: \nException: \nStack: ", exceptionMessage);
            Assert.Contains("safe text", formattedText);
        }
    }
}

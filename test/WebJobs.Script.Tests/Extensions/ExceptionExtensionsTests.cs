// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Moq;
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

        [Theory]
        [InlineData("alice@example.invalid")]
        [InlineData("ALICE@EXAMPLE.INVALID")]
        [InlineData("alice+tag@sub.example.invalid and bob@example.org")]
        [InlineData("o'brien@example.invalid")]
        [InlineData("AccountKey=synthetic-secret' alice@example.invalid")]
        [InlineData("alice@example.invalid AccountKey=synthetic-secret'")]
        [InlineData("https://synthetic-user:synthetic-password@host.invalid/path alice@example.invalid")]
        [InlineData("no email or credentials")]
        [InlineData("already [Hidden Email] and [Hidden Credential]")]
        [InlineData("caf\u00e9 \u652f\u6301 alice@example.invalid")]
        [InlineData("first alice@example.invalid\r\nsecond bob@example.org")]
        [InlineData("\"alice@example.invalid\" C:\\data\\file")]
        [InlineData("\0alice@example.invalid")]
        public void GetExceptionDetails_Rpc_RedactsRemoteMessageRepeatedInStack(string value)
        {
            const string customerMarker = "customer-record-12345";
            string remoteMessage = $"{customerMarker}: {value}";
            string stack = $"Error: {remoteMessage}\n    at execute (probe.js:42:7)\nRepeated: {remoteMessage}";
            var rpcException = new RpcException("Failure", remoteMessage, stack);
            var exception = new FunctionInvocationException("Invocation failed", rpcException);
            string originalMessage = rpcException.Message;
            string originalRemoteMessage = rpcException.RemoteMessage;
            string hash = EncryptionHelper.GetSHA256Base64String(Encoding.UTF8.GetBytes(originalRemoteMessage));
            string redacted = $"[Redacted]- Customers using AppInsights or OTel can view full details. (Hash: {hash})";

            var (exceptionType, exceptionMessage, exceptionDetails, formattedMessage) = exception.GetSanitizedExceptionDetails("safe text");

            Assert.Equal(typeof(RpcException).ToString(), exceptionType);
            foreach (string output in new[] { exceptionMessage, exceptionDetails })
            {
                Assert.Contains($"Exception: {redacted}", output);
                Assert.Contains($"Stack: Error: {redacted}", output);
                Assert.Contains($"Repeated: {redacted}", output);
                Assert.Contains("at execute (probe.js:42:7)", output);
                Assert.DoesNotContain(customerMarker, output);
            }

            Assert.Equal("safe text", formattedMessage);
            Assert.Equal(originalMessage, rpcException.Message);
            Assert.Equal(originalRemoteMessage, rpcException.RemoteMessage);
            Assert.Equal(stack, rpcException.RemoteStackTrace);
        }

        [Fact]
        public void GetExceptionDetails_Rpc_RedactsRemoteMessageWithInvalidUtf16()
        {
            GetExceptionDetails_Rpc_RedactsRemoteMessageRepeatedInStack("\uD800alice@example.invalid\uDC00");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GetExceptionDetails_Rpc_RemoteMessageTimeoutRedactsEntireOutput(bool headerTimedOut)
        {
            const string remoteMessage = "customer-record-12345 alice@example.invalid";
            string stack = $"Error: {remoteMessage}\n    at execute (probe.js:42:7)";
            var rpcException = new RpcException("Failure", headerTimedOut ? Sanitizer.EmailSanitizationTimeoutReplacement : remoteMessage, stack)
            {
                RemoteMessage = Sanitizer.EmailSanitizationTimeoutReplacement
            };
            var exception = new FunctionInvocationException("Invocation failed", rpcException);

            var (exceptionType, exceptionMessage, exceptionDetails, formattedMessage) = exception.GetSanitizedExceptionDetails("safe text");

            Assert.Equal(typeof(RpcException).ToString(), exceptionType);
            Assert.Equal(Sanitizer.EmailSanitizationTimeoutReplacement, exceptionMessage);
            Assert.Equal(Sanitizer.EmailSanitizationTimeoutReplacement, exceptionDetails);
            Assert.Equal("safe text", formattedMessage);
            Assert.Equal(stack, rpcException.RemoteStackTrace);
        }

        [Fact]
        public void GetExceptionDetails_Rpc_PreservesNormalizedTimeoutMarker()
        {
            var rpcException = new Mock<RpcException>("Failure", "email", "stack", string.Empty, false)
            {
                CallBase = true
            };
            rpcException.SetupGet(e => e.Message).Returns(Sanitizer.EmailSanitizationTimeoutReplacement);
            var exception = new FunctionInvocationException("Invocation failed", rpcException.Object);

            var (_, exceptionMessage, _, _) = exception.GetSanitizedExceptionDetails("safe text");

            Assert.Equal(Sanitizer.EmailSanitizationTimeoutReplacement, exceptionMessage);
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

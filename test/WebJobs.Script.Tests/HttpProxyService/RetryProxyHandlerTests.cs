// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Http
{
    public class RetryProxyHandlerTests
    {
        [Fact]
        public async Task SendAsync_RetriesToMax()
        {
            var inner = new TestHandler();
            var handler = new RetryProxyHandler(inner, NullLogger.Instance);
            var request = new HttpRequestMessage();

            var response = typeof(RetryProxyHandler)!
                .GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(handler, new object[] { request, CancellationToken.None })
                as Task<HttpResponseMessage>;

            var result = await response.ContinueWith(t => t);

            Assert.True(result.IsFaulted);
            Assert.True(result.Exception.InnerException is HttpRequestException);
            Assert.Equal(RetryProxyHandler.MaxRetries, inner.Attempts);
        }

        [Fact]
        public async Task SendAsync_StopsRetriesWhenScriptInvocationResultIsFaulted()
        {
            var inner = new TestHandler();
            var handler = new RetryProxyHandler(inner, NullLogger.Instance);
            var request = new HttpRequestMessage();

            // Create a faulted TaskCompletionSource for ScriptInvocationResult
            var faultedResultSource = new TaskCompletionSource<ScriptInvocationResult>();
            var invocationException = new InvalidOperationException("Function invocation failed");
            faultedResultSource.SetException(invocationException);

            // Create ScriptInvocationContext with faulted result source
            var scriptInvocationContext = new ScriptInvocationContext
            {
                ExecutionContext = new ExecutionContext
                {
                    InvocationId = Guid.NewGuid()
                },
                ResultSource = faultedResultSource
            };

            // Add the context to the request options
            request.Options.TryAdd(ScriptConstants.HttpProxyScriptInvocationContext, scriptInvocationContext);

            var response = typeof(RetryProxyHandler)!
                .GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(handler, new object[] { request, CancellationToken.None })
                as Task<HttpResponseMessage>;

            var result = await response.ContinueWith(t => t);

            // Verify that the task is faulted due to the ScriptInvocationResult being faulted
            Assert.True(result.IsFaulted);

            // Verify that no retries were attempted since the result source was already faulted
            Assert.Equal(0, inner.Attempts);
        }

        [Fact]
        public async Task SendAsync_TaskCanceledException_ThrowsOperationCanceledException_WhenCancellationRequested()
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var inner = new TaskCanceledTestHandler();
            var handler = new RetryProxyHandler(inner, NullLogger.Instance);
            var request = new HttpRequestMessage();

            var sendAsync = typeof(RetryProxyHandler)!
                .GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var task = (Task<HttpResponseMessage>)sendAsync.Invoke(handler, new object[] { request, cts.Token });

            var ex = await Assert.ThrowsAsync<OperationCanceledException>(() => task);
            Assert.True(cts.Token.IsCancellationRequested);
            Assert.Equal(cts.Token, ex.CancellationToken);
        }

        private class TaskCanceledTestHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new TaskCanceledException();
            }
        }

        private class TestHandler : HttpMessageHandler
        {
            public int Attempts { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Attempts++;

                throw new HttpRequestException();
            }
        }
    }
}

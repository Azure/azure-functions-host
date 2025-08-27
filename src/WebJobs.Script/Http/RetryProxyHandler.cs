// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Http
{
    internal sealed class RetryProxyHandler : DelegatingHandler
    {
        // The maximum number of retries
        internal const int MaxRetries = 10;

        // The initial delay in milliseconds
        internal const int InitialDelay = 50;

        // The maximum delay in milliseconds
        internal const int MaximumDelay = 250;

        private readonly ILogger _logger;

        public RetryProxyHandler(HttpMessageHandler innerHandler, ILogger logger)
            : base(innerHandler)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            TaskCompletionSource<ScriptInvocationResult> resultSource = null;
            if (request.Options.TryGetValue(ScriptConstants.HttpProxyScriptInvocationContext, out ScriptInvocationContext scriptInvocationContext))
            {
                resultSource = scriptInvocationContext.ResultSource;
            }

            var currentDelay = InitialDelay;
            for (int attemptCount = 1; attemptCount <= MaxRetries; attemptCount++)
            {
                try
                {
                    if (resultSource is not null && (resultSource.Task.IsFaulted || resultSource.Task.IsCanceled))
                    {
                        ExceptionDispatchInfo.Capture(resultSource.Task.Exception).Throw();
                    }

                    return await base.SendAsync(request, cancellationToken);
                }
                catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Request was canceled. Stopping retries.");
                    throw new OperationCanceledException(cancellationToken);
                }
                catch (HttpRequestException) when (attemptCount < MaxRetries)
                {
                    if (resultSource is not null && (resultSource.Task.IsFaulted || resultSource.Task.IsCanceled))
                    {
                        _logger.LogWarning("HTTP request will not be retried. The associated function invocation has failed.");
                        throw;
                    }

                    _logger.LogWarning("Failed to proxy request to the worker. Retrying in {delay}ms. Attempt {attemptCount} of {maxRetries}.",
                        currentDelay, attemptCount, MaxRetries);

                    await Task.Delay(currentDelay, cancellationToken);

                    currentDelay = Math.Min(currentDelay * 2, MaximumDelay);
                }
                catch (Exception ex)
                {
                    var message = attemptCount == MaxRetries
                        ? "Reached the maximum retry count for worker request proxying. Error: {exception}"
                        : $"HTTP request will not be retried. Exception in {nameof(RetryProxyHandler)}: {{exception}}.";

                    _logger.LogWarning(message, ex);

                    throw;
                }
            }

            // This should never be reached.
            throw null;
        }
    }
}
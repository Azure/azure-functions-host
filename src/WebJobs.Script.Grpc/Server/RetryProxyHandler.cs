// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
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
            var currentDelay = InitialDelay;
            for (int attemptCount = 1; attemptCount <= MaxRetries; attemptCount++)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException) when (attemptCount < MaxRetries)
                {
                    _logger.LogWarning("Failed to proxy request to the worker. Retrying in {delay}ms. Attempt {attemptCount} of {maxRetries}.",
                        currentDelay, attemptCount, MaxRetries);

                    await Task.Delay(currentDelay, cancellationToken);

                    currentDelay = Math.Min(currentDelay * 2, MaximumDelay);
                }
                catch (Exception ex)
                {
                    var message = attemptCount == MaxRetries
                        ? "Reached the maximum retry count for worker request proxying. Error: {exception}"
                        : $"Unsupported exception type in {nameof(RetryProxyHandler)}. Request will not be retried. Exception: {{exception}}";

                    _logger.LogWarning(message, ex);

                    throw;
                }
            }

            // This should never be reached.
            throw null;
        }
    }
}
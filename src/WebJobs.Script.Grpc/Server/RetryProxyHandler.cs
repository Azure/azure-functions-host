// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal sealed class RetryProxyHandler : DelegatingHandler
    {
        // The maximum number of retries
        private readonly int _maxRetries = 3;

        // The initial delay in milliseconds
        private readonly int _initialDelay = 50;
        private readonly ILogger _logger;

        public RetryProxyHandler(HttpMessageHandler innerHandler, ILogger logger)
            : base(innerHandler)
        {
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var currentDelay = _initialDelay;
            for (int attemptCount = 1; attemptCount <= _maxRetries; attemptCount++)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (HttpRequestException) when (attemptCount < _maxRetries)
                {
                    currentDelay *= attemptCount;

                    _logger.LogWarning("Failed to proxy request to the worker. Retrying in {delay}ms. Attempt {attemptCount} of {maxRetries}.",
                        currentDelay, attemptCount, _maxRetries);

                    await Task.Delay(currentDelay, cancellationToken);
                }
            }

            // This should never be reached.
            throw null;
        }
    }
}
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal class ResponseContextItemsCheckMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;

        public ResponseContextItemsCheckMiddleware(RequestDelegate next, ILogger<ResponseContextItemsCheckMiddleware> logger)
        {
            _logger = logger;
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (_next != null)
            {
                await _next(context);
            }

            // Check response for duplicate http headers
            if (context.Items.TryGetValue(ScriptConstants.AzureFunctionsDuplicateHttpHeadersKey, out object value))
            {
                _logger.LogDebug($"Duplicate HTTP header from function invocation removed. Duplicate key(s): {value?.ToString()}.");
            }
        }
    }
}

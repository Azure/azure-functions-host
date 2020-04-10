// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal partial class ExceptionMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
        {
            _logger = logger;
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next.Invoke(context);
            }
            catch (Exception ex)
            {
                if (!(ex is FunctionInvocationException))
                {
                    // exceptions throw by function code are handled/logged elsewhere
                    // our goal here is to log exceptions coming from our own runtime
                    Logger.UnhandledHostError(_logger, ex);
                }

                // We can't do anything if the response has already started, just abort.
                if (context.Response.HasStarted)
                {
                    Logger.ResponseStarted(_logger);
                    throw;
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                if (ex is HttpException httpException)
                {
                    context.Response.StatusCode = httpException.StatusCode;
                }
            }
        }
    }
}

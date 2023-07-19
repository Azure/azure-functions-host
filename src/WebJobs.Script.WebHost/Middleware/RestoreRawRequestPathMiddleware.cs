// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// Middleware to restore the raw request URL path received, in the current request URL.
    /// App service front end decodes encoded strings in the request URL path.
    /// This causes routing to fail if the route param value has %2F in it.
    /// Refer below issues for more context:
    /// https://github.com/dotnet/aspnetcore/issues/40532#issuecomment-1083562919
    /// https://github.com/Azure/azure-functions-host/issues/9290.
    /// </summary>
    internal class RestoreRawRequestPathMiddleware
    {
        private readonly RequestDelegate _next;
        internal const string UnEncodedUrlPathHeaderName = "X-Waws-Unencoded-Url";

        public RestoreRawRequestPathMiddleware(RequestDelegate next)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
        }

        public async Task Invoke(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(UnEncodedUrlPathHeaderName, out var unencodedUrlValue) &&
                unencodedUrlValue.Count > 0)
            {
                context.Request.Path = new PathString(unencodedUrlValue.First());
            }

            await _next(context);
        }
    }
}
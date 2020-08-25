// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE_APACHE.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.Extensions;

namespace Microsoft.AspNetCore.Buffering
{
    public class ResponseBufferingMiddleware
    {
        private readonly RequestDelegate _next;

        public ResponseBufferingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.IsAdminDownloadRequest())
            {
                await _next(httpContext);   // Otherwise /admin/functions/download will throw exception at BufferingWriteStream.get_Position() because this is not implemented in https://github.com/aspnet/KestrelHttpServer/blob/master/src/Kestrel.Core/Internal/Http/HttpResponseStream.cs#L34
            }
            else
            {
                var originalResponseBody = httpContext.Response.Body;

                // no-op if buffering is already available.
                if (originalResponseBody.CanSeek)
                {
                    await _next(httpContext);
                    return;
                }

                var originalBufferingFeature = httpContext.Features.Get<IHttpBufferingFeature>();
                try
                {
                    // Shim the response stream
                    var bufferStream = new BufferingWriteStream(originalResponseBody);
                    httpContext.Response.Body = bufferStream;
                    httpContext.Features.Set<IHttpBufferingFeature>(new HttpBufferingFeature(bufferStream, originalBufferingFeature));

                    await _next(httpContext);

                    // If we're still buffered, set the content-length header and flush the buffer.
                    // Only if the content-length header is not already set, and some content was buffered.
                    if (!httpContext.Response.HasStarted && bufferStream.CanSeek && bufferStream.Length > 0)
                    {
                        if (!httpContext.Response.ContentLength.HasValue)
                        {
                            httpContext.Response.ContentLength = bufferStream.Length;
                        }
                        await bufferStream.FlushAsync();
                    }
                }
                finally
                {
                    // undo everything
                    httpContext.Features.Set(originalBufferingFeature);
                    httpContext.Response.Body = originalResponseBody;
                }
            }
        }
    }
}

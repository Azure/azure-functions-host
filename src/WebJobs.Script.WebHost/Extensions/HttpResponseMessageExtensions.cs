// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Extensions
{
    public static class HttpResponseMessageExtensions
    {
        public static void SetEntityTagHeader(this HttpResponseMessage httpResponseMessage, EntityTagHeaderValue etag, DateTime lastModified)
        {
            if (httpResponseMessage.Content == null)
            {
                httpResponseMessage.Content = new NullContent();
            }

            httpResponseMessage.Headers.ETag = etag;
            httpResponseMessage.Content.Headers.LastModified = lastModified;
        }

        private class NullContent : StringContent
        {
            public NullContent()
                : base(string.Empty)
            {
                Headers.ContentType = null;
                Headers.ContentLength = null;
            }
        }
    }
}
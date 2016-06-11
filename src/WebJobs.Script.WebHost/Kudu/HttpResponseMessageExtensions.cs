using System;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    internal static class HttpResponseMessageExtensions
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

        class NullContent : StringContent
        {
            public NullContent()
                : base(String.Empty)
            {
                Headers.ContentType = null;
                Headers.ContentLength = null;
            }
        }
    }
}

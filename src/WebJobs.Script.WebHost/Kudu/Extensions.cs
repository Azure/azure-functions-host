// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Web;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public static class Extensions
    {
        public static string EscapeHashCharacter(this string str)
        {
            return str.Replace("#", Uri.EscapeDataString("#"));
        }

        public static void AddCert(this X509Store store, X509Certificate2 cert)
        {
            store.Open(OpenFlags.MaxAllowed);
            store.Add(cert);
            store.Close();
        }

        public static bool IsFunctionsPortalRequest(this HttpRequest request)
        {
            return request.Headers[KuduConstants.FunctionsPortal] != null;
        }

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
                : base(String.Empty)
            {
                Headers.ContentType = null;
                Headers.ContentLength = null;
            }
        }
    }
}
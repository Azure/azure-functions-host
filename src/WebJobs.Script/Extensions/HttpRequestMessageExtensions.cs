// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class HttpRequestMessageExtensions
    {
        public static IDictionary<string, string> GetRawHeaders(this HttpRequestMessage request)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();

            var allHeadersRaw = request.Headers.ToString();
            var rawHeaderLines = allHeadersRaw.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var header in rawHeaderLines)
            {
                int idx = header.IndexOf(':');
                string name = header.Substring(0, idx);
                string value = header.Substring(idx + 1).Trim();
                headers.Add(name, value);
            }

            return headers;
        }
    }
}

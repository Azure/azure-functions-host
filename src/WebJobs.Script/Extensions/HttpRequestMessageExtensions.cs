// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class HttpRequestMessageExtensions
    {
        public static AuthorizationLevel GetAuthorizationLevel(this HttpRequestMessage request)
        {
            return request.GetRequestPropertyOrDefault<AuthorizationLevel>(ScriptConstants.AzureFunctionsHttpRequestAuthorizationLevel);
        }

        public static TValue GetRequestPropertyOrDefault<TValue>(this HttpRequestMessage request, string key)
        {
            object value = null;
            if (request.Properties.TryGetValue(key, out value))
            {
                return (TValue)value;
            }
            return default(TValue);
        }

        public static IDictionary<string, string> GetQueryParameterDictionary(this HttpRequestMessage request)
        {
            var keyValuePairs = request.GetQueryNameValuePairs();

            // last one wins for any duplicate query parameters
            return keyValuePairs.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, s => s.Last().Value, StringComparer.OrdinalIgnoreCase);
        }

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

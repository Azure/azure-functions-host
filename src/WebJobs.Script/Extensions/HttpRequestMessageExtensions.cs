// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class HttpRequestMessageExtensions
    {
        public static bool MatchRoute(this HttpRequestMessage request, string route)
        {
            return request.RequestUri.LocalPath.Trim('/').ToLowerInvariant().Equals(route.Trim('/').ToLowerInvariant());
        }

        public static AuthorizationLevel GetAuthorizationLevel(this HttpRequestMessage request)
        {
            return request.GetRequestPropertyOrDefault<AuthorizationLevel>(ScriptConstants.AzureFunctionsHttpRequestAuthorizationLevelKey);
        }

        public static string GetRequestId(this HttpRequestMessage request)
        {
            return request.GetRequestPropertyOrDefault<string>(ScriptConstants.AzureFunctionsRequestIdKey);
        }

        public static void SetAuthorizationLevel(this HttpRequestMessage request, AuthorizationLevel authorizationLevel)
        {
            request.Properties[ScriptConstants.AzureFunctionsHttpRequestAuthorizationLevelKey] = authorizationLevel;
        }

        public static void SetProperty(this HttpRequestMessage request, string propertyName, object value)
        {
            request.Properties[propertyName] = value;
        }

        public static T GetPropertyOrDefault<T>(this HttpRequestMessage request, string propertyName)
        {
            return request.GetRequestPropertyOrDefault<T>(propertyName);
        }

        public static bool IsAntaresInternalRequest(this HttpRequestMessage request)
        {
            if (!ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                return false;
            }

            // this header will *always* be present on requests originating externally (i.e. going
            // through the Anatares front end). For requests originating internally it will NOT be
            // present.
            return !request.Headers.Contains(ScriptConstants.AntaresLogIdHeaderName);
        }

        public static bool IsAuthDisabled(this HttpRequestMessage request)
        {
            return request.GetRequestPropertyOrDefault<bool>(ScriptConstants.AzureFunctionsHttpRequestAuthorizationDisabledKey);
        }

        /// <summary>
        /// Returns true if the specified request is authorized at a level equal to or greater than
        /// the specified level.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="levelToCheck">The level to check.</param>
        /// <param name="keyName">Optional key name if key based auth is being used</param>
        /// <returns>True if the request is authorized at the specified level,
        /// false otherwise.</returns>
        public static bool HasAuthorizationLevel(this HttpRequestMessage request, AuthorizationLevel levelToCheck, string keyName = null)
        {
            if (request.IsAuthDisabled() || levelToCheck == AuthorizationLevel.Anonymous)
            {
                // when auth is disabled or the required level is Anonymous
                // the request is authorized
                return true;
            }

            var requestAuthorizationLevel = request.GetAuthorizationLevel();
            if (requestAuthorizationLevel == AuthorizationLevel.Admin)
            {
                // requests authorized at admin level are always allowed
                return true;
            }

            if (keyName != null)
            {
                // if a key name is specified, make sure we have a match
                string requestKeyName = request.GetPropertyOrDefault<string>(ScriptConstants.AzureFunctionsHttpRequestKeyNameKey);
                if (string.Compare(keyName, requestKeyName) != 0)
                {
                    return false;
                }
            }

            // otherwise, the request level must exactly match the required level
            return requestAuthorizationLevel == levelToCheck;
        }

        public static string GetHeaderValueOrDefault(this HttpRequestMessage request, string headerName)
        {
            IEnumerable<string> values = null;
            if (request.Headers.TryGetValues(headerName, out values))
            {
                return values.First();
            }
            return null;
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
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var allHeadersRaw = request.Headers.ToString() + Environment.NewLine + request.Content?.Headers?.ToString();
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

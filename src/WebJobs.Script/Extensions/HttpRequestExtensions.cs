// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.Extensions
{
    public static class HttpRequestExtensions
    {
        public static AuthorizationLevel GetAuthorizationLevel(this HttpRequest request)
        {
            return request.GetRequestPropertyOrDefault<AuthorizationLevel>(ScriptConstants.AzureFunctionsHttpRequestAuthorizationLevelKey);
        }

        public static TValue GetRequestPropertyOrDefault<TValue>(this HttpRequest request, string key)
        {
            if (request.HttpContext != null &&
                request.HttpContext.Items.TryGetValue(key, out object value))
            {
                return (TValue)value;
            }
            return default(TValue);
        }

        public static string GetRequestId(this HttpRequest request)
        {
            return request.GetRequestPropertyOrDefault<string>(ScriptConstants.AzureFunctionsRequestIdKey);
        }

        public static string GetHeaderValueOrDefault(this HttpRequest request, string headerName)
        {
            StringValues values;
            if (request.Headers.TryGetValue(headerName, out values))
            {
                return values.First();
            }
            return null;
        }

        public static TValue GetItemOrDefault<TValue>(this HttpRequest request, string key)
        {
            object value = null;
            if (request.HttpContext.Items.TryGetValue(key, out value))
            {
                return (TValue)value;
            }
            return default(TValue);
        }

        public static bool IsAntaresInternalRequest(this HttpRequest request)
        {
            if (!ScriptSettingsManager.Instance.IsAzureEnvironment)
            {
                return false;
            }

            // this header will *always* be present on requests originating externally (i.e. going
            // through the Anatares front end). For requests originating internally it will NOT be
            // present.
            return !request.Headers.Keys.Contains(ScriptConstants.AntaresLogIdHeaderName);
        }

        public static bool IsColdStart(this HttpRequest request)
        {
            return !string.IsNullOrEmpty(request.GetHeaderValueOrDefault(ScriptConstants.AntaresColdStartHeaderName));
        }

        public static Uri GetRequestUri(this HttpRequest request) => new Uri(request.GetDisplayUrl());
    }
}

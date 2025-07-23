// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class GrpcMessageExtensionUtilities
    {
        private static readonly object BoxedTrue = true;
        private static readonly object BoxedFalse = false;
        private static readonly IReadOnlyDictionary<string, object> EmptyHeaders = new Dictionary<string, object>();

        public static ExpandoObject ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
        {
            if (inputMessage is null)
            {
                return null;
            }

            var expando = new ExpandoObject();
            IDictionary<string, object> dict = expando;

            dict["method"] = inputMessage.Method;
            dict["query"] = inputMessage.Query;
            dict["statusCode"] = inputMessage.StatusCode;
            dict["enableContentNegotiation"] = inputMessage.EnableContentNegotiation ? BoxedTrue : BoxedFalse;

            if (inputMessage.Headers is { Count: > 0 })
            {
                var headerDict = new Dictionary<string, object>(inputMessage.Headers.Count);
                foreach (var kvp in inputMessage.Headers)
                {
                    headerDict[kvp.Key] = kvp.Value;
                }
                dict["headers"] = headerDict;
            }
            else
            {
                dict["headers"] = EmptyHeaders;
            }

            if (inputMessage.Cookies is { Count: > 0 })
            {
                var cookiesList = new List<Tuple<string, string, CookieOptions>>(inputMessage.Cookies.Count);
                foreach (var cookie in inputMessage.Cookies)
                {
                    cookiesList.Add(RpcHttpCookieConverter(cookie));
                }
                dict["cookies"] = cookiesList;
            }
            else
            {
                dict["cookies"] = Array.Empty<Tuple<string, string, CookieOptions>>();
            }

            if (inputMessage.Body is not null)
            {
                dict["body"] = inputMessage.Body.ToObject();
            }

            return expando;
        }

        public static Tuple<string, string, CookieOptions> RpcHttpCookieConverter(RpcHttpCookie cookie)
        {
            var cookieOptions = new CookieOptions();
            if (cookie.Domain != null)
            {
                cookieOptions.Domain = cookie.Domain.Value;
            }

            if (cookie.Path != null)
            {
                cookieOptions.Path = cookie.Path.Value;
            }

            if (cookie.Secure != null)
            {
                cookieOptions.Secure = cookie.Secure.Value;
            }

            cookieOptions.SameSite = RpcSameSiteEnumConverter(cookie.SameSite);

            if (cookie.HttpOnly != null)
            {
                cookieOptions.HttpOnly = cookie.HttpOnly.Value;
            }

            if (cookie.Expires != null)
            {
                cookieOptions.Expires = cookie.Expires.Value.ToDateTimeOffset();
            }

            if (cookie.MaxAge != null)
            {
                cookieOptions.MaxAge = TimeSpan.FromSeconds(cookie.MaxAge.Value);
            }

            return new Tuple<string, string, CookieOptions>(cookie.Name, cookie.Value, cookieOptions);
        }

        internal static void UpdateWorkerMetadata(this WorkerMetadata workerMetadata, RpcWorkerConfig workerConfig)
        {
            workerMetadata.RuntimeName ??= workerConfig.Description.Language;
            workerMetadata.RuntimeVersion ??= workerConfig.Description.DefaultRuntimeVersion;
        }

        private static SameSiteMode RpcSameSiteEnumConverter(RpcHttpCookie.Types.SameSite sameSite) => sameSite switch
        {
            RpcHttpCookie.Types.SameSite.Strict => SameSiteMode.Strict,
            RpcHttpCookie.Types.SameSite.Lax => SameSiteMode.Lax,
            RpcHttpCookie.Types.SameSite.None => SameSiteMode.Unspecified,
            RpcHttpCookie.Types.SameSite.ExplicitNone => SameSiteMode.None,
            _ => SameSiteMode.Unspecified
        };
    }
}

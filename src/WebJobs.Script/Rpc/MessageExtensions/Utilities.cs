// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal static class Utilities
    {
        public static object ConvertFromHttpMessageToExpando(RpcHttp inputMessage)
        {
            if (inputMessage == null)
            {
                return null;
            }

            // TEST
            inputMessage.Cookies.Add(new RpcHttpCookie()
            {
                Name = "myname",
                Value = "myvalue",
                Expires = new NullableTimestamp()
                {
                    Value = new Timestamp()
                    {
                        Seconds = 60 * 60 * 12 * 200
                    }
                }
            });
            // end test input

            dynamic expando = new ExpandoObject();
            expando.method = inputMessage.Method;
            expando.query = inputMessage.Query as IDictionary<string, string>;
            expando.statusCode = inputMessage.StatusCode;
            expando.headers = inputMessage.Headers.ToDictionary(p => p.Key, p => (object)p.Value);
            expando.enableContentNegotiation = inputMessage.EnableContentNegotiation;
            expando.cookies = inputMessage.Cookies.ToList<RpcHttpCookie>();

            if (inputMessage.Body != null)
            {
                expando.body = inputMessage.Body.ToObject();
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
                try
                {
                    var age = TimeSpan.FromSeconds(cookie.MaxAge.Value);
                    cookieOptions.MaxAge = age;
                }
                catch (Exception e)
                {
                    // TODO: log warning that unparseable.
                    Console.WriteLine(e);
                }
            }

            return new Tuple<string, string, CookieOptions>(cookie.Name, cookie.Value, cookieOptions);
        }

        private static SameSiteMode RpcSameSiteEnumConverter(RpcHttpCookie.Types.SameSite sameSite)
        {
            switch (sameSite)
            {
                case RpcHttpCookie.Types.SameSite.Strict:
                    return SameSiteMode.Strict;
                case RpcHttpCookie.Types.SameSite.Lax:
                    return SameSiteMode.Lax;
                case RpcHttpCookie.Types.SameSite.None:
                    return SameSiteMode.None;
                default:
                    return SameSiteMode.None;
            }
        }
    }
}
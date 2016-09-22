﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Web;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public static class TraceExtensions
    {
        public const string AlwaysTrace = "alwaysTrace";
        public const string TraceLevelKey = "traceLevel";

        private static readonly Dictionary<string, string> Empty = new Dictionary<string, string>();

        // These are the set of attributes/headers that will be filtered out from trace.xml.
        private static readonly HashSet<string> BlackList = new HashSet<string>(new[]
        {
            AlwaysTrace,
            TraceLevelKey,
            "Max-Forwards",
            "X-LiveUpgrade",
            "X-ARR-LOG-ID",
            "DISGUISED-HOST",
            "X-Original-URL",
            "X-Forwarded-For",
            KuduConstants.SiteRestrictedJWT,
            "X-ARR-SSL"
        },
        StringComparer.OrdinalIgnoreCase);

        public static IDisposable Step(this ITracer tracer, string message)
        {
            return tracer.Step(message, Empty);
        }

        public static IDisposable Step(this ITracer tracer, string message, params object[] args)
        {
            return tracer.Step(String.Format(message, args), Empty);
        }

        public static void Trace(this ITracer tracer, string message, params object[] args)
        {
            tracer.Trace(String.Format(message, args), Empty);
        }

        public static void TraceError(this ITracer tracer, Exception ex, string format, params object[] args)
        {
            var attribs = GetErrorAttribute(ex, string.Format(CultureInfo.InvariantCulture, format, args));
            tracer.Trace("Error occurred", attribs);
        }

        public static void TraceError(this ITracer tracer, Exception ex)
        {
            tracer.Trace("Error occurred", GetErrorAttribute(ex));
        }

        public static void TraceError(this ITracer tracer, string message)
        {
            tracer.Trace("Error occurred", new Dictionary<string, string>
            {
                { "type", "error" },
                { "text", message }
            });
        }

        public static void TraceError(this ITracer tracer, string message, params object[] args)
        {
            tracer.Trace("Error occurred", new Dictionary<string, string>
            {
                { "type", "error" },
                { "text", String.Format(message, args) }
            });
        }

        public static void TraceWarning(this ITracer tracer, string message, params object[] args)
        {
            tracer.Trace("Warning", new Dictionary<string, string>
            {
                { "type", "warning" },
                { "text", String.Format(message, args) }
            });
        }

        public static bool ShouldTrace(this ITracer tracer, IDictionary<string, string> attributes)
        {
            return tracer.TraceLevel >= TraceLevel.Verbose || tracer.TraceLevel >= GetTraceLevel(attributes) || attributes.ContainsKey(AlwaysTrace);
        }

        public static void TraceProcessExitCode(this ITracer tracer, Process process)
        {
            int exitCode = process.ExitCode;

            // Don't trace success exit code, which needlessly pollute the trace
            if (exitCode != 0)
            {
                tracer.Trace("Process dump", new Dictionary<string, string>
                {
                    { "exitCode", process.ExitCode.ToString() },
                    { "type", "processOutput" }
                });
            }
        }

        // Some attributes only carry control information and are not meant for display
        public static bool IsNonDisplayableAttribute(string key)
        {
            return BlackList.Contains(key);
        }

        public static bool ShouldSkipRequest(HttpRequestBase request)
        {
            // Filter out pings to applications.
            if (request.RawUrl == "/")
            {
                return true;
            }

            if (String.IsNullOrEmpty(request.UserAgent))
            {
                return false;
            }

            // Skip tracing direct browsers requests.
            return request.UserAgent.StartsWith("Mozilla", StringComparison.OrdinalIgnoreCase) ||
                    request.UserAgent.StartsWith("Opera", StringComparison.OrdinalIgnoreCase);
        }

        // From System.Web.Mvc.AjaxRequestExtensions.IsAjaxRequest
        public static bool IsAjaxRequest(HttpRequestBase request)
        {
            return String.Equals("XMLHttpRequest", request.Headers["X-REQUESTED-WITH"], StringComparison.OrdinalIgnoreCase);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "Referer")]
        public static bool MismatchedHostReferer(HttpRequest request)
        {
            var referer = request.Headers["Referer"];

            // referer is not present if already same origin
            if (String.IsNullOrEmpty(referer))
            {
                return false;
            }

            // referer can be invalid uri (such as literally 'null' in some case)
            Uri refererUri;
            if (!Uri.TryCreate(referer, UriKind.Absolute, out refererUri))
            {
                return true;
            }

            return !String.Equals(request.Url.Host, refererUri.Host, StringComparison.OrdinalIgnoreCase);
        }

        private static TraceLevel GetTraceLevel(IDictionary<string, string> attributes)
        {
            string type;
            attributes.TryGetValue("type", out type);

            if (type == "error")
            {
                return TraceLevel.Error;
            }

            string value;
            if (attributes.TryGetValue(TraceLevelKey, out value))
            {
                var traceLevel = Int32.Parse(value);
                if (traceLevel <= (int)TraceLevel.Error)
                {
                    return TraceLevel.Error;
                }
                else if (traceLevel <= (int)TraceLevel.Info)
                {
                    return TraceLevel.Info;
                }
            }

            return TraceLevel.Verbose;
        }

        private static Dictionary<string, string> GetErrorAttribute(Exception ex, string message = null)
        {
            string errorMessage = null;
            if (string.IsNullOrWhiteSpace(message))
            {
                errorMessage = ex.Message;
            }
            else
            {
                errorMessage = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", message, ex.Message);
            }

            var attribs = new Dictionary<string, string>
            {
                { "type", "error" },
                { "text", errorMessage },
                { "stackTrace", ex.StackTrace ?? String.Empty }
            };

            if (ex.InnerException != null)
            {
                attribs["innerText"] = ex.InnerException.Message;
                attribs["innerStackTrace"] = ex.InnerException.StackTrace ?? String.Empty;
            }

            return attribs;
        }
    }
}
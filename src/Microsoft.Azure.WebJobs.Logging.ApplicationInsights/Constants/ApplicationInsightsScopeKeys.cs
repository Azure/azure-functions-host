// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Logging.ApplicationInsights
{
    internal static class ApplicationInsightsScopeKeys
    {
        internal const string HttpRequest = "MS_HttpRequest";

        // HTTP context is set automatically by ASP.NET, this isn't ours.
        internal const string HttpContext = "MS_HttpContext";

        // This is set by Functions
        internal const string FunctionsHttpResponse = "MS_AzureFunctionsHttpResponse";

        internal const string ForwardedForHeaderName = "X-Forwarded-For";
    }
}

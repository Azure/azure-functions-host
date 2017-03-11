// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Loggers
{
    internal static class ScopeKeys
    {
        // These are used internally for passing values via scopes
        public const string FunctionInvocationId = "MS_FunctionInvocationId";
        public const string FunctionName = "MS_FunctionName";
        public const string HttpRequest = "MS_HttpRequest";

        // HTTP context is set automatically by ASP.NET, this isn't ours.
        internal const string HttpContext = "MS_HttpContext";

        // This is set by Functions
        internal const string FunctionsHttpResponse = "MS_AzureFunctionsHttpResponse";

        internal const string ForwardedForHeaderName = "X-Forwarded-For";
    }
}

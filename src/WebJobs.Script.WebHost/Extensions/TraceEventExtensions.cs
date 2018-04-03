// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class TraceEventExtensions
    {
        public static string GetPropertyValueOrNull(this TraceEvent traceEvent, string key)
        {
            if (traceEvent != null && traceEvent.Properties.TryGetValue(key, out object value))
            {
                return value?.ToString();
            }

            return null;
        }

        public static (string exceptionType, string exceptionMessage, string exceptionDetails) GetExceptionDetails(this TraceEvent traceEvent)
        {
            if (traceEvent.Exception == null)
            {
                return (null, null, null);
            }

            // Find the inner-most exception
            Exception innerException = traceEvent.Exception;
            while (innerException.InnerException != null)
            {
                innerException = innerException.InnerException;
            }

            string exceptionType = innerException.GetType().ToString();
            string exceptionMessage = Sanitizer.Sanitize(innerException.Message);
            string exceptionDetails = Sanitizer.Sanitize(innerException.ToFormattedString());

            return (exceptionType, exceptionMessage, exceptionDetails);
        }
    }
}
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;

namespace System
{
    internal static class ExceptionExtensions
    {
        public static (string exceptionType, string exceptionMessage, string exceptionDetails) GetExceptionDetails(this Exception exception)
        {
            if (exception == null)
            {
                return (null, null, null);
            }

            // Find the inner-most exception
            Exception innerException = exception;
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

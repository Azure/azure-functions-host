// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace System
{
    internal static class ExceptionExtensions
    {
        private const string RedactedMessage = "[Redacted]- Customers using AppInsights or OTel can view full details.";

        public static (string ExceptionType, string ExceptionMessage, string ExceptionDetails) GetExceptionDetails(this Exception exception)
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
            string exceptionDetails = Sanitizer.Sanitize(exception.ToFormattedString());

            return (exceptionType, exceptionMessage, exceptionDetails);
        }

        /// <summary>
        /// For FunctionInvocationException with innermost exception as RpcException, the remote message segment is replaced with a redacted
        /// placeholder containing a stable hash so that occurrences can still be correlated without exposing the original content.
        /// </summary>
        /// <param name="exception">
        /// The exception instance to inspect. Must not be null.
        /// </param>
        /// <param name="formattedMessage">
        /// A pre-formatted message.
        /// </param>
        /// <returns>
        /// A tuple containing:
        /// (InnerExceptionType) The full CLR type name of the base exception.
        /// (InnerExceptionMessage) The sanitized and safe base exception message.
        /// (Details) The sanitized and safe formatted exception string.
        /// (FormattedMessage) The sanitized version of the provided formattedMessage parameter.
        /// </returns>
        public static (string InnerExceptionType, string InnerExceptionMessage, string Details, string FormattedMessage)
            GetSanitizedExceptionDetails(this Exception exception, string formattedMessage)
        {
            ArgumentNullException.ThrowIfNull(exception);
            formattedMessage = Sanitizer.Sanitize(formattedMessage);

            var baseException = exception.GetBaseException();
            var innerType = baseException.GetType().ToString();
            var originalMessage = baseException.Message;
            var formattedDetails = exception.ToFormattedString();

            if (exception is FunctionInvocationException && baseException is RpcException { RemoteMessage: var remoteMsg }
                && !string.IsNullOrWhiteSpace(remoteMsg))
            {
                var redacted = GetRedactedExceptionMessage(remoteMsg);

                var innerExceptionMessage = string.IsNullOrWhiteSpace(originalMessage)
                    ? string.Empty
                    : Sanitizer.Sanitize(originalMessage.Replace(remoteMsg, redacted, StringComparison.Ordinal));

                var detailsSanitized = string.IsNullOrWhiteSpace(formattedDetails)
                    ? string.Empty
                    : Sanitizer.Sanitize(formattedDetails.Replace(remoteMsg, redacted, StringComparison.Ordinal));

                return (innerType, innerExceptionMessage, detailsSanitized, formattedMessage);
            }

            var defaultInnerExceptionMessage = Sanitizer.Sanitize(originalMessage);
            var defaultDetails = Sanitizer.Sanitize(formattedDetails);

            return (innerType, defaultInnerExceptionMessage, defaultDetails, formattedMessage);
        }

        private static string GetRedactedExceptionMessage(string msg)
        {
            return $"{RedactedMessage} (Hash: {EncryptionHelper.GetSHA256Base64String(Encoding.UTF8.GetBytes(msg))})";
        }
    }
}

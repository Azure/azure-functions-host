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
        /// Returns sanitized exception details. The remote message segment is replaced with a redacted placeholder containing a stable hash so that
        /// occurrences can still be correlated without exposing the original content.
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

            return exception is FunctionInvocationException
                && baseException is RpcException { RemoteMessage: var remoteMsg } && remoteMsg is not null
                ? (innerType,
                    Sanitizer.Sanitize(originalMessage.Replace(remoteMsg, GetRedactedExceptionMessage(remoteMsg), StringComparison.Ordinal)),
                    Sanitizer.Sanitize(formattedDetails.Replace(remoteMsg, GetRedactedExceptionMessage(remoteMsg), StringComparison.Ordinal)),
                    formattedMessage)
                : (innerType,
                    Sanitizer.Sanitize(originalMessage),
                    Sanitizer.Sanitize(formattedDetails),
                    formattedMessage);
        }

        private static string GetRedactedExceptionMessage(string msg)
        {
            return $"{RedactedMessage} (Hash: {EncryptionHelper.GetSHA256Base64String(Encoding.UTF8.GetBytes(msg))})";
        }
    }
}

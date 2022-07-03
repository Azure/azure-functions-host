// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core
{
    internal class RpcActionError
    {
        internal RpcErrorCode Type { get; set; } = RpcErrorCode.None;

        internal string ConciseMessage { get; set; } = string.Empty;

        internal string VerboseMessage { get; set; } = string.Empty;

        internal string Advice { get; set; } = string.Empty;
    }

    internal enum RpcErrorCode
    {
        None,
        Message_Not_Sent_Error,
        Message_Not_Received_Error,
        Validation_Error,
    }

    internal static class RpcErrorConstants
    {
        public static string MessageNotReceived = "No \"{0}\" message from language worker matches the given criteria.";
        // TODO: to replace with real link
        public static string MessageNotReceivedLink = "https://github.com/azure/azure-functions-host";

        public static string ValidationFailed = "The \"{0}\" message that matches the given criteria fails validation.";
        // TODO: to replace with real link
        public static string ValidationFailedLink = "https://github.com/azure/azure-functions-host";

        public static string MessageNotSent = "The \"{0}\" message fails to send to languague worker.";
        // TODO: to replace with real link
        public static string MessageNotSentLink = "https://github.com/azure/azure-functions-host";

        public static string GeneralErrorAdvice = "For more information on {0}, please visit {1}";

    }
}

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
        public static string MessageNotReceived = "The worker did not emit a message of type \"{0}\" that meets the matchingCriteria.";
        public static string MessageNotReceivedLink = "https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#message_not_received_error";

        public static string ValidationFailed = "The \"{0}\" message that matches the given criteria fails at least one of the validators.";
        public static string ValidationFailedLink = "https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#validation_error";

        public static string MessageNotSent = "The \"{0}\" message fails to send to languague worker.";
        public static string MessageNotSentLink = "https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#message_not_sent_error";

        public static string GeneralErrorAdvice = "For more information, please visit {0}";

    }
}

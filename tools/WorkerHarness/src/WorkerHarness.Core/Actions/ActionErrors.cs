// Copyright(c).NET Foundation.All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Actions
{
    internal static class ActionErrors
    {
        internal static string ValidationErrorMessage = "The message failed the validation: {0} == {1}. The query result is different from the expected value";

        internal static string WorkerNotExitMessage = "Worker process has not exited despite waiting for {0} seconds";
        internal static string WorkerNotExitAdvice = "Check if your worker supports graceful shutdowns. If so, you may want to increase the grace period";

        internal static string MessageNotReceiveErrorMessage = "The worker did not emit a StreamingMessage of type \"{0}\" that meets the matching criteria";

        internal static string MessageNotSentErrorMessage = "The harness cannot create a StreamingMessage of type \"{0}\" with the given payload";
        internal static string MessageNotSentErrorAdvice = "If the payload contains variables, it's possible that the variables did not exist when the harness attempted to create the message";

        internal static string DisplayVerboseErrorAdvice = "Consider setting the \"DisplayVerboseError\" to \"true\" in the harness.settings.json file";
        internal static string GeneralErrorAdvice = "For more information, please visit https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#errors";
    }

    internal enum ActionErrorCode
    {
        None,
        MessageNotSentError,
        MessageNotReceivedError,
        ValidationError,
        WorkerNotExitError,
        ArgumentError
    }
}

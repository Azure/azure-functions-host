// Copyright(c).NET Foundation.All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Actions
{
    internal static class ActionErrors
    {
        public static string ValidationFailed = "The worker emitted the expected message of type \"{0}\" that meets the matchingCriteria but fails at least one of the validators.";
        public static string ValidationFailedVerbose = "The worker emitted the expected message of type \"{0}\" that meets the matchingCriteria but fails at least one of the validators. \nThe failed message is: {1}";

        internal static string WorkerNotExitMessage = "Worker process has not exited despite waiting for {0} seconds";
        internal static string WorkerNotExitAdvice = "Check if your worker supports graceful shutdowns. If so, you may want to increase the grace period";

        public static string GeneralErrorAdvice = "For more information, please visit https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#errors";
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

// Copyright(c).NET Foundation.All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Actions
{
    internal static class ActionErrors
    {
        internal static string WorkerNotExitMessage = "Worker process has not exited despite waiting for {0} seconds";
        internal static string WorkerNotExitAdvice = "Check if your worker supports graceful shutdowns. If so, you may want to increase the grace period";
        internal static string WorkerNotExitLink = "https://github.com/Azure/azure-functions-host/tree/features/harness/tools/WorkerHarness#worker_not_exit_error";
    }

    internal enum ActionErrorCode
    {
        None,
        Message_Not_Sent_Error,
        Message_Not_Received_Error,
        Validation_Error,
        Worker_Not_Exit_Error
    }
}

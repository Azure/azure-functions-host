using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkerHarness.Core
{
    public enum StatusCode
    {
        Success,
        Failure,
    }

    internal enum Error
    {
        None,
        Message_Not_Sent_Error,
        Message_Not_Received_Error,
        Validation_Error,
    }

    internal static class ActionConstants
    {
        public static string MessageNotReceived = "No \"{0}\" message that matches the given criteria is received.";
        // TODO: to replace with real link
        public static string MessageNotReceivedLink = "https://github.com/azure/azure-functions-host";

        public static string ValidationFailed = "The \"{0}\" message that matches the given criteria fails to pass the given validators.";
        // TODO: to replace with real link
        public static string ValidationFailedLink = "https://github.com/azure/azure-functions-host";

        public static string MessageNotSent = "The \"{0}\" message fails to send to languague worker.";
        // TODO: to replace with real link
        public static string MessageNotSentLink = "https://github.com/azure/azure-functions-host";

        public static string VerboseFlagRecommendation = "Consider turning on the --verbose flag to see additional message information captured by the harness.";

    }

}

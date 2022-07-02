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

        // TODO: to delete
        Error,
        Timeout
    }

    internal enum Error
    {
        None,
        Message_Not_Sent_Error,
        Message_Not_Received_Error,
        Validation_Error,
    }

    internal class ActionState
    {
        public StatusCode Status { get; set; }

        public Error Error { get; set; }

        public string Message { get; set; } = string.Empty;

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

    public class ActionResult
    {
        // TODO: delete
        public string ActionType { get; set; }
        // TODO: delete
        public string ActionName { get; set; }
        // TODO: delete
        public StatusCode Status { get; set; }
        // TODO: delete
        public IList<string> Messages { get; set; }
        // TODO: delete
        public ActionResult(string actionType, string actionName)
        {
            ActionType = actionType;
            ActionName = actionName;
            Messages = new List<string>();
        }
    }
}

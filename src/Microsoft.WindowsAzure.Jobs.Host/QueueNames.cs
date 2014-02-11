using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class QueueNames
    {
        private const string QueuePrefix = "azure-jobs-";

        private const string InvokeQueuePrefix = QueuePrefix + "invoke-";

        public static string GetInvokeQueueName(Guid hostId)
        {
            return InvokeQueuePrefix + hostId.ToString("N");
        }
    }
}

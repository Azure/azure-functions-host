using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class QueueNames
    {
        private const string Prefix = "azure-jobs-";

        private const string InvokeQueuePrefix = Prefix + "invoke-";

        public static string GetInvokeQueueName(Guid hostId)
        {
            return InvokeQueuePrefix + hostId.ToString("N");
        }
    }
}

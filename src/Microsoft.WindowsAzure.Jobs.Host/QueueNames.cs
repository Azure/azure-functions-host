using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class QueueNames
    {
        private const string Prefix = "azure-jobs-";

        private const string HostQueuePrefix = Prefix + "host-";

        public static string GetHostQueueName(Guid hostId)
        {
            return HostQueuePrefix + hostId.ToString("N");
        }
    }
}

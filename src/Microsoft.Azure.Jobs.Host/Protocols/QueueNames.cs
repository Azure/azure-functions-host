using System;

namespace Microsoft.Azure.Jobs.Host.Protocols
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

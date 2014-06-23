using System;

namespace Microsoft.Azure.Jobs.Host
{
    internal static class HostQueueNames
    {
        private const string Prefix = "azure-jobs-";

        private const string HostQueuePrefix = Prefix + "host-";

        /// <summary>Gets the host instance queue name.</summary>
        /// <param name="hostId">The host ID.</param>
        /// <returns>The host instance queue name.</returns>
        public static string GetHostQueueName(Guid hostId)
        {
            return HostQueuePrefix + hostId.ToString("N");
        }
    }
}

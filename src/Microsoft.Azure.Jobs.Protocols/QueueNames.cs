using System;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Provides well-known queue names in the protocol.</summary>
#if PUBLICPROTOCOL
    public static class QueueNames
#else
    internal static class QueueNames
#endif
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

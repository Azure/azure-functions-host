using System;

#if PUBLICSTORAGE
namespace Microsoft.Azure.Jobs.Storage.Queue
#else
namespace Microsoft.Azure.Jobs.Host.Storage.Queue
#endif
{
    /// <summary>Defines a queue client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface ICloudQueueClient
#else
    internal interface ICloudQueueClient
#endif
    {
        /// <summary>Gets a queue reference.</summary>
        /// <param name="queueName">The queue name.</param>
        /// <returns>A queue reference.</returns>
        ICloudQueue GetQueueReference(string queueName);
    }
}

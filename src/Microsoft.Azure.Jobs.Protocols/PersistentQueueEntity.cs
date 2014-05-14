using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a table entity for a persistent queue message.</summary>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class PersistentQueueEntity : TableEntity
#else
    internal class PersistentQueueEntity : TableEntity
#endif
    {
        /// <summary>Gets or sets the ID of the message.</summary>
        public Guid MessageId { get; set; }

        /// <summary>Gets or sets the time the message was inserted into the queue.</summary>
        public DateTimeOffset? OriginalTimestamp { get; set; }

        /// <summary>Returns the partition key.</summary>
        /// <returns>The partition key.</returns>
        public static string GetPartitionKey()
        {
            return String.Empty;
        }

        /// <summary>Returns a row key.</summary>
        /// <param name="nextVisibleOn">The next time the message will be visible.</param>
        /// <param name="messageId">The message ID.</param>
        /// <returns>The row key.</returns>
        public static string GetRowKey(DateTime nextVisibleOn, Guid messageId)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:u}_{1:n}", nextVisibleOn, messageId);
        }
    }
}
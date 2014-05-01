using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class PersistentQueueEntity : TableEntity
    {
        public Guid MessageId { get; set; }

        public DateTimeOffset? OriginalTimestamp { get; set; }

        public static string GetPartitionKey()
        {
            return String.Empty;
        }

        public static string GetRowKey(DateTime nextVisibleOn, Guid messageId)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:u}_{1:n}", nextVisibleOn, messageId);
        }
    }
}
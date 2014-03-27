using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class PersistentQueueEntity : TableServiceEntity
    {
        public Guid MessageId { get; set; }

        public DateTime? OriginalTimestamp { get; set; }

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
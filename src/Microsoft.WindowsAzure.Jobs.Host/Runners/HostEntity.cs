using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal class HostEntity : TableServiceEntity
    {
        public Guid Id { get; set; }
    }
}

using System;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class HostEntity : TableServiceEntity
    {
        public Guid Id { get; set; }
    }
}

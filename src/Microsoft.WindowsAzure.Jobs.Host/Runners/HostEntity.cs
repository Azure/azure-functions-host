using System;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal class HostEntity : TableServiceEntity
    {
        public Guid Id { get; set; }
    }
}

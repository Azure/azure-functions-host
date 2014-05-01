using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class HostEntity : TableEntity
    {
        public Guid Id { get; set; }
    }
}

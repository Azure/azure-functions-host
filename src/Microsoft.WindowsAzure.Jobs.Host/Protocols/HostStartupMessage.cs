using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class HostStartupMessage : PersistentQueueMessage
    {
        public Guid HostInstanceId { get; set; }
        public Guid HostId { get; set; }
        public IEnumerable<FunctionDefinition> Functions { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class HostStartupMessage : PersistentQueueMessage
    {
        public Guid HostInstanceId { get; set; }
        public Guid HostId { get; set; }
        public IEnumerable<FunctionDefinition> Functions { get; set; }
    }
}

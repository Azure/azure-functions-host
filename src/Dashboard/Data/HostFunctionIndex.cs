using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class HostFunctionIndex
    {
        public Guid HostId { get; set; }

        public DateTimeOffset HostVersion { get; set; }

        public List<string> FunctionIds { get; set; }

        public List<VersionedFunction> OldFunctions { get; set; }

        public List<VersionedFunction> PendingInsertions { get; set; }
    }
}

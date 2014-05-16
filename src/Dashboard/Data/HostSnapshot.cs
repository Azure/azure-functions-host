using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class HostSnapshot
    {
        public DateTimeOffset HostVersion { get; set; }

        public IEnumerable<FunctionSnapshot> Functions { get; set; }
    }
}

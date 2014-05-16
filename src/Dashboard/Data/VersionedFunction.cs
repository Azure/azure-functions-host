using System;

namespace Dashboard.Data
{
    public class VersionedFunction
    {
        public string FunctionId { get; set; }

        public DateTimeOffset HostVersion { get; set; }
    }
}

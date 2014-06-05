using System;
using System.Collections.Generic;

namespace Dashboard.Data
{
    public class FunctionSnapshot
    {
        public string Id { get; set; }

        public string QueueName { get; set; }

        public string HostFunctionId { get; set; }

        public string FullName { get; set; }

        public string ShortName { get; set; }

        public IDictionary<string, ParameterSnapshot> Parameters { get; set; }
    }
}

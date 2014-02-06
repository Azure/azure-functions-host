using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Jobs.Runners;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class InvocationMessage
    {
        public InvocationMessageType Type { get; set; }

        public Guid Id { get; set; }

        public string FunctionId { get; set; }

        public IDictionary<string, string> Arguments { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace Microsoft.WindowsAzure.Jobs.Host.Runners
{
    internal class InvocationMessage
    {
        public InvocationMessageType Type { get; set; }

        public Guid Id { get; set; }

        public string FunctionId { get; set; }

        public IDictionary<string, string> Arguments { get; set; }
    }
}

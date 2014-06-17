using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    internal class FunctionExecutionContext
    {
        public HostOutputMessage HostOutputMessage { get; set; }

        public IFunctionOutputLogDispenser OutputLogDispenser { get; set; }

        public IFunctionInstanceLogger FunctionInstanceLogger { get; set; }
    }
}

using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class ConsoleFunctionOuputLogFactory : IFunctionOutputLogger
    {
        public IFunctionOutputDefinition Create(IFunctionInstance instance)
        {
            return new ConsoleFunctionOutputDefinition();
        }
    }
}

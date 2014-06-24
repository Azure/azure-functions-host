using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // Implementation of IFunctionOuputLogDispenser that just logs to the console. 
    class ConsoleFunctionOuputLogDispenser : IFunctionOutputLogDispenser
    {
        public FunctionOutputLog CreateLogStream(IFunctionInstance instance)
        {
            return new FunctionOutputLog();
        }
    }
}

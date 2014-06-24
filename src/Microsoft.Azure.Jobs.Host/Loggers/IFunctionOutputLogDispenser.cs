using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    // Interface for creating objects that capture a function execution's Console output. 
    internal interface IFunctionOutputLogDispenser
    {
        FunctionOutputLog CreateLogStream(IFunctionInstance instance);
    }
}

using Microsoft.Azure.Jobs.Host.Executors;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal interface IFunctionOutputLogger
    {
        IFunctionOutputDefinition Create(IFunctionInstance instance);
    }
}

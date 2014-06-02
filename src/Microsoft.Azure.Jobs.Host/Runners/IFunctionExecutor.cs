using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IFunctionExecutor
    {
        void Execute(IReadOnlyDictionary<string, object> parameters);
    }
}

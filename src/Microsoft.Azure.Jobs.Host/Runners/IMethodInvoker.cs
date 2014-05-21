using System.Collections.Generic;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IMethodInvoker
    {
        void Invoke(IReadOnlyDictionary<string, BindResult> parameterProviders);
    }
}

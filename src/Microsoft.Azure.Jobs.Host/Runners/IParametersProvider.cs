using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IParametersProvider
    {
        FunctionDefinition Function { get; }

        IReadOnlyDictionary<string, IValueProvider> Bind();
    }
}

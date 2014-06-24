using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Indexers;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal interface IParametersProvider
    {
        FunctionDefinition Function { get; }

        IReadOnlyDictionary<string, IValueProvider> Bind();
    }
}

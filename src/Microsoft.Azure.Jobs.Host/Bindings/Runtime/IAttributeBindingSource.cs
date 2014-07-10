using System;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal interface IAttributeBindingSource
    {
        BindingContext BindingContext { get; }

        CancellationToken CancellationToken { get; }

        IBinding Bind<TValue>(Attribute attribute);
    }
}

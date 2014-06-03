using System;
using System.Threading;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IAttributeBinding
    {
        CancellationToken CancellationToken { get; }

        IValueProvider Bind<TValue>(Attribute attribute);
    }
}

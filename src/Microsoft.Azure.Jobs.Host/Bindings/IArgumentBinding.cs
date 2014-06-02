using System;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IArgumentBinding<T>
    {
        Type ValueType { get; }

        IValueProvider Bind(T value, ArgumentBindingContext context);
    }
}

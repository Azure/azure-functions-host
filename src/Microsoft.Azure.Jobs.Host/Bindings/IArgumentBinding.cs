using System;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IArgumentBinding<TArgument>
    {
        Type ValueType { get; }

        IValueProvider Bind(TArgument value, FunctionBindingContext context);
    }
}

using System;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal interface IValueProvider
    {
        Type Type { get; }

        object GetValue();

        string ToInvokeString();
    }
}

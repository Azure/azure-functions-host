using System;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal interface ITableEntityArgumentBindingProvider
    {
        IArgumentBinding<TableEntityContext> TryCreate(Type parameterType);
    }
}

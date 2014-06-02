using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal interface ITableArgumentBindingProvider
    {
        IArgumentBinding<CloudTable> TryCreate(Type parameterType);
    }
}

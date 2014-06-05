using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class CloudTableArgumentBindingProvider : ITableArgumentBindingProvider
    {
        public IArgumentBinding<CloudTable> TryCreate(Type parameterType)
        {
            if (parameterType != typeof(CloudTable))
            {
                return null;
            }

            return new CloudTableArgumentBinding();
        }

        private class CloudTableArgumentBinding : IArgumentBinding<CloudTable>
        {
            public Type ValueType
            {
                get { return typeof(CloudTable); }
            }

            public IValueProvider Bind(CloudTable value, ArgumentBindingContext context)
            {
                value.CreateIfNotExists();
                return new TableValueProvider(value, value, typeof(CloudTable));
            }
        }
    }
}

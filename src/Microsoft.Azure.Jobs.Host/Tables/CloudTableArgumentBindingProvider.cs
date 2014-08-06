// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
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

            public async Task<IValueProvider> BindAsync(CloudTable value, ValueBindingContext context)
            {
                await value.CreateIfNotExistsAsync(context.CancellationToken);
                return new TableValueProvider(value, value, typeof(CloudTable));
            }
        }
    }
}

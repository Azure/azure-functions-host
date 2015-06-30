// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class StorageTableArgumentBindingProvider : IStorageTableArgumentBindingProvider
    {
        public IStorageTableArgumentBinding TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(IStorageTable))
            {
                return null;
            }

            return new StorageTableArgumentBinding();
        }

        private class StorageTableArgumentBinding : IStorageTableArgumentBinding
        {
            public Type ValueType
            {
                get { return typeof(IStorageTable); }
            }

            public FileAccess Access
            {
                get
                {
                    return FileAccess.ReadWrite;
                }
            }

            public Task<IValueProvider> BindAsync(IStorageTable value, ValueBindingContext context)
            {
                IValueProvider valueProvider = new TableValueProvider(value, value, typeof(IStorageTable));
                return Task.FromResult(valueProvider);
            }
        }
    }
}

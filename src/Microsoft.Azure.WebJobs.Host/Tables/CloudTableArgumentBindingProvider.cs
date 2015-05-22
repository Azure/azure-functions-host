// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class CloudTableArgumentBindingProvider : ITableArgumentBindingProvider
    {
        public ITableArgumentBinding TryCreate(Type parameterType)
        {
            if (parameterType != typeof(CloudTable))
            {
                return null;
            }

            return new CloudTableArgumentBinding();
        }

        private class CloudTableArgumentBinding : ITableArgumentBinding
        {
            public Type ValueType
            {
                get { return typeof(CloudTable); }
            }

            public FileAccess Access
            {
                get
                {
                    return FileAccess.ReadWrite;
                }
            }

            public async Task<IValueProvider> BindAsync(IStorageTable value, ValueBindingContext context)
            {
                await value.CreateIfNotExistsAsync(context.CancellationToken);
                return new TableValueProvider(value, value.SdkObject, typeof(CloudTable));
            }
        }
    }
}

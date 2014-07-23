// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBindingProvider : IBindingProvider
    {
        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (context.Parameter.ParameterType != typeof(CloudStorageAccount))
            {
                return Task.FromResult<IBinding>(null);
            }

            IBinding binding = new CloudStorageAccountBinding(parameter.Name, context.StorageAccount);
            return Task.FromResult(binding);
        }
    }
}

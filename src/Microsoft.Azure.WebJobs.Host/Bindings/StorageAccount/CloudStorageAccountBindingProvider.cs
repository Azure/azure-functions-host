// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBindingProvider : IBindingProvider
    {
        private readonly IStorageAccountProvider _accountProvider;

        public CloudStorageAccountBindingProvider(IStorageAccountProvider accountProvider)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            _accountProvider = accountProvider;
        }

        public async Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (context.Parameter.ParameterType != typeof(CloudStorageAccount))
            {
                return null;
            }

            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            IBinding binding = new CloudStorageAccountBinding(parameter.Name, account.SdkObject);
            return binding;
        }
    }
}

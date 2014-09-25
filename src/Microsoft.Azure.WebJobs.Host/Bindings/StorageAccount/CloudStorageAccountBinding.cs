// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.WebJobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBinding : IBinding
    {
        private static readonly IObjectToTypeConverter<CloudStorageAccount> _converter =
            new CompositeObjectToTypeConverter<CloudStorageAccount>(
                new OutputConverter<CloudStorageAccount>(new IdentityConverter<CloudStorageAccount>()),
                new OutputConverter<string>(new StringToCloudStorageAccountConverter()));

        private readonly string _parameterName;
        private readonly string _accountName;

        public CloudStorageAccountBinding(string parameterName, CloudStorageAccount account)
        {
            _parameterName = parameterName;
            _accountName = StorageClient.GetAccountName(account);
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private Task<IValueProvider> BindAccountAsync(CloudStorageAccount account, ValueBindingContext context)
        {
            IValueProvider provider = new CloudStorageAccountValueProvider(account);
            return Task.FromResult(provider);
        }

        public Task<IValueProvider> BindAsync(object value, ValueBindingContext context)
        {
            CloudStorageAccount account = null;

            if (!_converter.TryConvert(value, out account))
            {
                throw new InvalidOperationException("Unable to convert value to CloudStorageAccount.");
            }

            return BindAccountAsync(account, context);
        }

        public Task<IValueProvider> BindAsync(BindingContext context)
        {
            return BindAccountAsync(context.StorageAccount.SdkObject, context.ValueContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new CloudStorageAccountParameterDescriptor
            {
                Name = _parameterName,
                AccountName = _accountName
            };
        }
    }
}

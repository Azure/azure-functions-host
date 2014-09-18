// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class BlobAttributeBindingProvider : IBindingProvider
    {
        private readonly IBlobArgumentBindingProvider _provider;

        public BlobAttributeBindingProvider(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            _provider = CreateProvider(cloudBlobStreamBinderTypes);
        }

        private static IBlobArgumentBindingProvider CreateProvider(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            List<IBlobArgumentBindingProvider> innerProviders = new List<IBlobArgumentBindingProvider>();

            innerProviders.Add(CreateConverterProvider<ICloudBlob, IdentityConverter<ICloudBlob>>());
            innerProviders.Add(CreateConverterProvider<CloudBlockBlob, CloudBlobToCloudBlockBlobConverter>());
            innerProviders.Add(CreateConverterProvider<CloudPageBlob, CloudBlobToCloudPageBlobConverter>());
            innerProviders.Add(new StreamArgumentBindingProvider(defaultAccess: null));
            innerProviders.Add(new CloudBlobStreamArgumentBindingProvider());
            innerProviders.Add(new TextReaderArgumentBindingProvider());
            innerProviders.Add(new TextWriterArgumentBindingProvider());
            innerProviders.Add(new StringArgumentBindingProvider());
            innerProviders.Add(new OutStringArgumentBindingProvider());

            if (cloudBlobStreamBinderTypes != null)
            {
                foreach (Type cloudBlobStreamBinderType in cloudBlobStreamBinderTypes)
                {
                    Type itemType;
                    ICloudBlobStreamObjectBinder objectBinder =
                        CloudBlobStreamObjectBinder.Create(cloudBlobStreamBinderType, out itemType);

                    innerProviders.Add(new ObjectArgumentBindingProvider(objectBinder, itemType));
                    innerProviders.Add(new OutObjectArgumentBindingProvider(objectBinder, itemType));
                }
            }

            return new CompositeArgumentBindingProvider(innerProviders);
        }

        private static IBlobArgumentBindingProvider CreateConverterProvider<TValue, TConverter>()
            where TConverter : IConverter<ICloudBlob, TValue>, new()
        {
            return new ConverterArgumentBindingProvider<TValue>(new TConverter());
        }

        public Task<IBinding> TryCreateAsync(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            BlobAttribute blob = parameter.GetCustomAttribute<BlobAttribute>(inherit: false);

            if (blob == null)
            {
                return Task.FromResult<IBinding>(null);
            }

            string resolvedCombinedPath = context.Resolve(blob.BlobPath);
            IBindableBlobPath path = BindableBlobPath.Create(resolvedCombinedPath);
            path.ValidateContractCompatibility(context.BindingDataContract);

            IBlobArgumentBinding argumentBinding = _provider.TryCreate(parameter, blob.Access);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Blob to type '" + parameter.ParameterType + "'.");
            }

            IBinding binding = new BlobBinding(parameter.Name, argumentBinding,
                context.StorageAccount.CreateCloudBlobClient(), path);
            return Task.FromResult(binding);
        }
    }
}

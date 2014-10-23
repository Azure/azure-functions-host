// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Triggers
{
    internal class BlobTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private readonly INameResolver _nameResolver;
        private readonly IStorageAccountProvider _accountProvider;
        private readonly IBlobArgumentBindingProvider _provider;
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IQueueConfiguration _queueConfiguration;

        public BlobTriggerAttributeBindingProvider(INameResolver nameResolver, IStorageAccountProvider accountProvider,
            IExtensionTypeLocator extensionTypeLocator, IHostIdProvider hostIdProvider,
            IQueueConfiguration queueConfiguration)
        {
            if (accountProvider == null)
            {
                throw new ArgumentNullException("accountProvider");
            }

            if (extensionTypeLocator == null)
            {
                throw new ArgumentNullException("extensionTypeLocator");
            }

            if (hostIdProvider == null)
            {
                throw new ArgumentNullException("hostIdProvider");
            }

            if (queueConfiguration == null)
            {
                throw new ArgumentNullException("queueConfiguration");
            }

            _nameResolver = nameResolver;
            _accountProvider = accountProvider;
            _provider = CreateProvider(extensionTypeLocator.GetCloudBlobStreamBinderTypes());
            _hostIdProvider = hostIdProvider;
            _queueConfiguration = queueConfiguration;
        }

        private static IBlobArgumentBindingProvider CreateProvider(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            List<IBlobArgumentBindingProvider> innerProviders = new List<IBlobArgumentBindingProvider>();

            innerProviders.Add(CreateConverterProvider<ICloudBlob, StorageBlobToCloudBlobConverter>());
            innerProviders.Add(CreateConverterProvider<CloudBlockBlob, StorageBlobToCloudBlockBlobConverter>());
            innerProviders.Add(CreateConverterProvider<CloudPageBlob, StorageBlobToCloudPageBlobConverter>());
            innerProviders.Add(new StreamArgumentBindingProvider(defaultAccess: FileAccess.Read));
            innerProviders.Add(new TextReaderArgumentBindingProvider());
            innerProviders.Add(new StringArgumentBindingProvider());

            if (cloudBlobStreamBinderTypes != null)
            {
                innerProviders.AddRange(cloudBlobStreamBinderTypes.Select(
                    t => CloudBlobStreamObjectBinder.CreateReadBindingProvider(t)));
            }

            return new CompositeArgumentBindingProvider(innerProviders);
        }

        private static IBlobArgumentBindingProvider CreateConverterProvider<TValue, TConverter>()
            where TConverter : IConverter<IStorageBlob, TValue>, new()
        {
            return new ConverterArgumentBindingProvider<TValue>(new TConverter());
        }

        public async Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            BlobTriggerAttribute blobTrigger = parameter.GetCustomAttribute<BlobTriggerAttribute>(inherit: false);

            if (blobTrigger == null)
            {
                return null;
            }

            string resolvedCombinedPath = Resolve(blobTrigger.BlobPath);
            IBlobPathSource path = BlobPathSource.Create(resolvedCombinedPath);

            IArgumentBinding<IStorageBlob> argumentBinding = _provider.TryCreate(parameter, access: null);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind BlobTrigger to type '" + parameter.ParameterType + "'.");
            }

            IStorageAccount account = await _accountProvider.GetStorageAccountAsync(context.CancellationToken);
            ITriggerBinding binding = new BlobTriggerBinding(parameter.Name, argumentBinding, account, path,
                _hostIdProvider, _queueConfiguration);
            return binding;
        }

        private string Resolve(string queueName)
        {
            if (_nameResolver == null)
            {
                return queueName;
            }

            return _nameResolver.ResolveWholeString(queueName);
        }
    }
}

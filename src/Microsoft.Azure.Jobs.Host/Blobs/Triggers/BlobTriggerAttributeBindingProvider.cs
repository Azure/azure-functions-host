using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal class BlobTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private readonly IBlobArgumentBindingProvider _provider;

        public BlobTriggerAttributeBindingProvider(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            _provider = CreateProvider(cloudBlobStreamBinderTypes);
        }

        private static IBlobArgumentBindingProvider CreateProvider(IEnumerable<Type> cloudBlobStreamBinderTypes)
        {
            List<IBlobArgumentBindingProvider> innerProviders = new List<IBlobArgumentBindingProvider>();

            innerProviders.Add(new ConverterBlobArgumentBindingProvider<ICloudBlob>(new IdentityConverter<ICloudBlob>()));
            innerProviders.Add(new ConverterBlobArgumentBindingProvider<CloudBlockBlob>(new CloudBlobToCloudBlockBlobConverter()));
            innerProviders.Add(new ConverterBlobArgumentBindingProvider<CloudPageBlob>(new CloudBlobToCloudPageBlobConverter()));
            innerProviders.Add(new StreamBlobArgumentBindingProvider());
            innerProviders.Add(new TextReaderBlobArgumentBindingProvider());
            innerProviders.Add(new StringBlobArgumentBindingProvider());

            if (cloudBlobStreamBinderTypes != null)
            {
                foreach (Type cloudBlobStreamBinderType in cloudBlobStreamBinderTypes)
                {
                    innerProviders.Add(new ObjectBlobArgumentBindingProvider(cloudBlobStreamBinderType));
                }
            }

            return new CompositeBlobArgumentBindingProvider(innerProviders);
        }

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            BlobTriggerAttribute blobTrigger = parameter.GetCustomAttribute<BlobTriggerAttribute>();

            if (blobTrigger == null)
            {
                return null;
            }

            string blobPath = context.Resolve(blobTrigger.BlobPath);
            CloudBlobPath parsedBlobPath = Parse(blobPath);

            Type parameterType = parameter.ParameterType;
            IArgumentBinding<ICloudBlob> argumentBinding = _provider.TryCreate(parameterType);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind BlobTrigger to type '" + parameterType + "'.");
            }

            return new BlobTriggerBinding(argumentBinding, context.StorageAccount, parsedBlobPath.ContainerName, parsedBlobPath.BlobName);
        }

        CloudBlobPath Parse(string blobPath)
        {
            return new CloudBlobPath(blobPath);
        }
    }
}

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

            innerProviders.Add(new ConverterArgumentBindingProvider<ICloudBlob>(new IdentityConverter<ICloudBlob>()));
            innerProviders.Add(new ConverterArgumentBindingProvider<CloudBlockBlob>(new CloudBlobToCloudBlockBlobConverter()));
            innerProviders.Add(new ConverterArgumentBindingProvider<CloudPageBlob>(new CloudBlobToCloudPageBlobConverter()));
            innerProviders.Add(new StreamArgumentBindingProvider());
            innerProviders.Add(new TextReaderArgumentBindingProvider());
            innerProviders.Add(new StringArgumentBindingProvider());

            if (cloudBlobStreamBinderTypes != null)
            {
                foreach (Type cloudBlobStreamBinderType in cloudBlobStreamBinderTypes)
                {
                    innerProviders.Add(new ObjectArgumentBindingProvider(cloudBlobStreamBinderType));
                }
            }

            return new CompositeArgumentBindingProvider(innerProviders);
        }

        public ITriggerBinding TryCreate(TriggerBindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            BlobTriggerAttribute blobTrigger = parameter.GetCustomAttribute<BlobTriggerAttribute>(inherit: false);

            if (blobTrigger == null)
            {
                return null;
            }

            string blobPath = context.Resolve(blobTrigger.BlobPath);
            CloudBlobPath parsedBlobPath = Parse(blobPath);

            IArgumentBinding<ICloudBlob> argumentBinding = _provider.TryCreate(parameter, access: null);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind BlobTrigger to type '" + parameter.ParameterType + "'.");
            }

            return new BlobTriggerBinding(parameter.Name, argumentBinding,
                context.StorageAccount.CreateCloudBlobClient(), parsedBlobPath.ContainerName, parsedBlobPath.BlobName);
        }

        private static CloudBlobPath Parse(string blobPath)
        {
            return new CloudBlobPath(blobPath);
        }
    }
}

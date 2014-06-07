using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
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

            innerProviders.Add(new ConverterArgumentBindingProvider<ICloudBlob>(new IdentityConverter<ICloudBlob>()));
            innerProviders.Add(new ConverterArgumentBindingProvider<CloudBlockBlob>(new CloudBlobToCloudBlockBlobConverter()));
            innerProviders.Add(new ConverterArgumentBindingProvider<CloudPageBlob>(new CloudBlobToCloudPageBlobConverter()));
            innerProviders.Add(new StreamArgumentBindingProvider());
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

        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;
            BlobAttribute blob = parameter.GetCustomAttribute<BlobAttribute>(inherit: false);

            if (blob == null)
            {
                return null;
            }

            string blobPath = context.Resolve(blob.BlobPath);
            CloudBlobPath parsedBlobPath = Parse(blobPath);

            if (!parsedBlobPath.HasParameters())
            {
                BlobClient.ValidateContainerName(parsedBlobPath.ContainerName);
                BlobClient.ValidateBlobName(parsedBlobPath.BlobName);
            }

            foreach (string parameterName in parsedBlobPath.GetParameterNames())
            {
                if (context.BindingDataContract != null && !context.BindingDataContract.ContainsKey(parameterName))
                {
                    throw new InvalidOperationException("No binding parameter exists for '" + parameterName + "'.");
                }
            }

            IArgumentBinding<ICloudBlob> argumentBinding = _provider.TryCreate(parameter);

            if (argumentBinding == null)
            {
                throw new InvalidOperationException("Can't bind Blob to type '" + parameter.ParameterType + "'.");
            }

            return new BlobBinding(argumentBinding, context.StorageAccount.CreateCloudBlobClient(),
                parsedBlobPath.ContainerName, parsedBlobPath.BlobName, parameter.IsOut);
        }

        private static CloudBlobPath Parse(string blobPath)
        {
            return new CloudBlobPath(blobPath);
        }
    }
}

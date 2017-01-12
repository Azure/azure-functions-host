// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class CloudBlobDirectoryArgumentBindingProvider : IBlobContainerArgumentBindingProvider
    {
        public IArgumentBinding<IStorageBlobContainer> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(CloudBlobDirectory))
            {
                return new CloudBlobDirectoryArgumentBinding();
            }

            return null;
        }

        private class CloudBlobDirectoryArgumentBinding : IArgumentBinding<IStorageBlobContainer>
        {
            public Type ValueType
            {
                get { return typeof(CloudBlobDirectory); }
            }

            public Task<IValueProvider> BindAsync(IStorageBlobContainer container, ValueBindingContext context)
            {
                if (container == null)
                {
                    throw new ArgumentNullException("container");
                }

                BlobValueBindingContext blobValueBindingContext = (BlobValueBindingContext)context;
                CloudBlobDirectory directory = container.SdkObject.GetDirectoryReference(blobValueBindingContext.Path.BlobName);

                return Task.FromResult<IValueProvider>(new ValueProvider(directory));
            }

            private class ValueProvider : IValueProvider
            {
                private readonly CloudBlobDirectory _directory;

                public ValueProvider(CloudBlobDirectory directory)
                {
                    _directory = directory;
                }

                public Type Type
                {
                    get
                    {
                        return typeof(CloudBlobDirectory);
                    }
                }

                public Task<object> GetValueAsync()
                {
                    return Task.FromResult<object>(_directory);
                }

                public string ToInvokeString()
                {
                    return _directory.Uri.AbsolutePath;
                }
            }
        }
    }
}

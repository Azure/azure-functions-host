// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class CloudBlobContainerArgumentBindingProvider : IBlobContainerArgumentBindingProvider
    {
        public IArgumentBinding<IStorageBlobContainer> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(CloudBlobContainer))
            {
                return new CloudBlobContainerArgumentBinding();
            }

            return null;
        }

        private class CloudBlobContainerArgumentBinding : IArgumentBinding<IStorageBlobContainer>
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(CloudBlobContainer); }
            }

            public Task<IValueProvider> BindAsync(IStorageBlobContainer container, ValueBindingContext context)
            {
                if (container == null)
                {
                    throw new ArgumentNullException("container");
                }

                return Task.FromResult<IValueProvider>(new ValueProvider(container.SdkObject));
            }

            private class ValueProvider : IValueProvider
            {
                private readonly CloudBlobContainer _container;

                public ValueProvider(CloudBlobContainer container)
                {
                    _container = container;
                }

                public Type Type
                {
                    get
                    {
                        return typeof(CloudBlobContainer);
                    }
                }

                public object GetValue()
                {
                    return _container;
                }

                public string ToInvokeString()
                {
                    return _container.Uri.ToString();
                }
            }
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class CloudBlobEnumerableArgumentBindingProvider : IBlobContainerArgumentBindingProvider
    {
        public IArgumentBinding<IStorageBlobContainer> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(IEnumerable<ICloudBlob>) ||
                parameter.ParameterType == typeof(IEnumerable<CloudBlockBlob>) ||
                parameter.ParameterType == typeof(IEnumerable<CloudPageBlob>) ||
                parameter.ParameterType == typeof(IEnumerable<CloudAppendBlob>) ||
                parameter.ParameterType == typeof(IEnumerable<TextReader>) ||
                parameter.ParameterType == typeof(IEnumerable<Stream>) ||
                parameter.ParameterType == typeof(IEnumerable<string>))
            {
                return new CloudBlobEnumerableArgumentBinding(parameter);
            }

            return null;
        }

        private class CloudBlobEnumerableArgumentBinding : IArgumentBinding<IStorageBlobContainer>
        {
            private readonly ParameterInfo _parameter;

            public CloudBlobEnumerableArgumentBinding(ParameterInfo parameter)
            {
                _parameter = parameter;
            }

            public Type ValueType
            {
                get { return typeof(IEnumerable<CloudBlockBlob>); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlobContainer container, ValueBindingContext context)
            {
                BlobValueBindingContext containerBindingContext = (BlobValueBindingContext)context;

                // Query the blob container using the blob prefix (if specified)
                // Note that we're explicitly using useFlatBlobListing=true to collapse
                // sub directories. If users want to bind to a sub directory, they can
                // bind to CloudBlobDirectory.
                string prefix = containerBindingContext.Path.BlobName;
                IEnumerable<IStorageListBlobItem> blobItems = await container.ListBlobsAsync(prefix, true, context.CancellationToken);

                // create an IEnumerable<T> of the correct type, performing any
                // required conversions on the blobs
                Type elementType = _parameter.ParameterType.GenericTypeArguments[0];
                IList list = await ConvertBlobs(elementType, blobItems);

                string invokeString = containerBindingContext.Path.ToString();
                return new ValueProvider(list, _parameter.ParameterType, invokeString);
            }

            private static async Task<IList> ConvertBlobs(Type targetType, IEnumerable<IStorageListBlobItem> blobItems)
            { 
                Type listType = typeof(List<>).MakeGenericType(targetType);
                IList list = (IList)Activator.CreateInstance(listType);
                foreach (var blobItem in blobItems)
                {
                    object converted = await ConvertBlob(targetType, ((IStorageBlob)blobItem).SdkObject);
                    list.Add(converted);
                }

                return list;
            }

            private static async Task<object> ConvertBlob(Type targetType, ICloudBlob blob)
            {
                object converted = blob;

                if (targetType == typeof(Stream))
                {
                    converted = await blob.OpenReadAsync();
                }
                else if (targetType == typeof(TextReader))
                {
                    Stream stream = await blob.OpenReadAsync();
                    converted = new StreamReader(stream);
                }
                else if (targetType == typeof(string))
                {
                    Stream stream = await blob.OpenReadAsync();
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        converted = await reader.ReadToEndAsync();
                    }
                }

                return converted;
            }

            private class ValueProvider : IValueProvider
            {
                private readonly object _value;
                private readonly Type _type;
                private readonly string _invokeString;

                public ValueProvider(object value, Type type, string invokeString)
                {
                    _value = value;
                    _type = type;
                    _invokeString = invokeString;
                }

                public Type Type
                {
                    get
                    {
                        return _type;
                    }
                }

                public object GetValue()
                {
                    return _value;
                }

                public string ToInvokeString()
                {
                    return _invokeString;
                }
            }
        }
    }
}

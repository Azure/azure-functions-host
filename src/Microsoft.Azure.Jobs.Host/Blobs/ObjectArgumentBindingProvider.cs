// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class ObjectArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly ICloudBlobStreamObjectBinder _objectBinder;
        private readonly Type _valueType;

        public ObjectArgumentBindingProvider(Type cloudBlobStreamBinderType)
        {
            _objectBinder = CloudBlobStreamObjectBinder.Create(cloudBlobStreamBinderType, out _valueType);
        }

        public ObjectArgumentBindingProvider(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
        {
            _objectBinder = objectBinder;
            _valueType = valueType;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != _valueType)
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to custom object using access "
                    + access.Value.ToString() + ".");
            }

            return new ObjectArgumentBinding(_objectBinder, _valueType);
        }

        private class ObjectArgumentBinding : IBlobArgumentBinding
        {
            private readonly ICloudBlobStreamObjectBinder _objectBinder;
            private readonly Type _valueType;

            public ObjectArgumentBinding(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
            {
                _objectBinder = objectBinder;
                _valueType = valueType;
            }

            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                object value;
                ParameterLog status;

                using (Stream rawStream = await blob.OpenReadAsync(context.CancellationToken))
                using (WatchableReadStream watchableStream = new WatchableReadStream(rawStream))
                {
                    value = await _objectBinder.ReadFromStreamAsync(watchableStream, context.CancellationToken);
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, _valueType, new ImmutableWatcher(status));
            }
        }
    }
}

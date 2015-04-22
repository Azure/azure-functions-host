// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class ObjectArgumentBindingProvider<TValue, TBinder> : IBlobArgumentBindingProvider
        where TBinder : ICloudBlobStreamBinder<TValue>, new()
    {
        private readonly ICloudBlobStreamBinder<TValue> _objectBinder = new TBinder();

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(TValue))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to custom object using access "
                    + access.Value.ToString() + ".");
            }

            return new ObjectArgumentBinding(_objectBinder);
        }

        private class ObjectArgumentBinding : IBlobArgumentBinding
        {
            private readonly ICloudBlobStreamBinder<TValue> _objectBinder;

            public ObjectArgumentBinding(ICloudBlobStreamBinder<TValue> objectBinder)
            {
                _objectBinder = objectBinder;
            }

            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(TValue); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                TValue value;
                
                WatchableReadStream watchableStream = await ReadBlobArgumentBinding.TryBindStreamAsync(blob, context);
                if (watchableStream == null)
                {
                    value = await _objectBinder.ReadFromStreamAsync(watchableStream, context.CancellationToken);
                    return BlobValueProvider.Create(blob, value);
                }

                ParameterLog status;

                using (watchableStream)
                {
                    value = await _objectBinder.ReadFromStreamAsync(watchableStream, context.CancellationToken);
                    status = watchableStream.GetStatus();
                }

                return BlobWatchableValueProvider.Create(blob, value, new ImmutableWatcher(status));
            }
        }
    }
}

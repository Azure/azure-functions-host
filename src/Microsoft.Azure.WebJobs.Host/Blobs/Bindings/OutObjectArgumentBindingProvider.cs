// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class OutObjectArgumentBindingProvider<TValue, TBinder> : IBlobArgumentBindingProvider
        where TBinder : ICloudBlobStreamBinder<TValue>, new()
    {
        private readonly ICloudBlobStreamBinder<TValue> _objectBinder = new TBinder();

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (!parameter.IsOut || parameter.ParameterType.GetElementType() != typeof(TValue))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind blob out parameter using access "
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
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(TValue); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(
                    blob, context);
                return new ObjectValueBinder(blob, watchableStream, _objectBinder);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class ObjectValueBinder : IValueBinder, IWatchable
            {
                private readonly IStorageBlob _blob;
                private readonly WatchableCloudBlobStream _stream;
                private readonly ICloudBlobStreamBinder<TValue> _objectBinder;

                public ObjectValueBinder(IStorageBlob blob, WatchableCloudBlobStream stream,
                    ICloudBlobStreamBinder<TValue> objectBinder)
                {
                    _blob = blob;
                    _stream = stream;
                    _objectBinder = objectBinder;
                }

                public Type Type
                {
                    get { return typeof(TValue); }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return default(TValue);
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    await _objectBinder.WriteToStreamAsync((TValue)value, _stream, cancellationToken);

                    if (!_stream.HasCommitted)
                    {
                        await _stream.CommitAsync(cancellationToken);
                    }

                    _stream.Dispose();
                }

                public string ToInvokeString()
                {
                    return _blob.GetBlobPath();
                }
            }
        }
    }
}

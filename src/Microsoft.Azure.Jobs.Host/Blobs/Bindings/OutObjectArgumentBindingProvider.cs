// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class OutObjectArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly ICloudBlobStreamObjectBinder _objectBinder;
        private readonly Type _valueType;

        public OutObjectArgumentBindingProvider(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
        {
            _objectBinder = objectBinder;
            _valueType = valueType;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (!parameter.IsOut || parameter.ParameterType.GetElementType() != _valueType)
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind blob out parameter using access "
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
                _valueType = valueType;
                _objectBinder = objectBinder;
            }

            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, FunctionBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob using an ICloudBlobStreamBinder.");
                }

                CloudBlobStream rawStream = await blockBlob.OpenWriteAsync(context.CancellationToken);
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.BlobWrittenWatcher);
                WatchableCloudBlobStream watchableStream = new WatchableCloudBlobStream(rawStream, committedAction);
                return new ObjectValueBinder(blob, watchableStream, _objectBinder, _valueType);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class ObjectValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly WatchableCloudBlobStream _stream;
                private readonly ICloudBlobStreamObjectBinder _objectBinder;
                private readonly Type _valueType;

                public ObjectValueBinder(ICloudBlob blob, WatchableCloudBlobStream stream,
                    ICloudBlobStreamObjectBinder objectBinder, Type valueType)
                {
                    _blob = blob;
                    _stream = stream;
                    _objectBinder = objectBinder;
                    _valueType = valueType;
                }

                public Type Type
                {
                    get { return _valueType; }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return null;
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    await _objectBinder.WriteToStreamAsync(_stream, value, cancellationToken);
                    await _stream.CommitAsync(cancellationToken);
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

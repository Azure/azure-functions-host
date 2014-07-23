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
    internal class CloudBlobStreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(CloudBlobStream))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind CloudBlobStream using access " +
                    access.Value.ToString() + ".");
            }

            return new CloudBlobStreamArgumentBinding();
        }

        private class CloudBlobStreamArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(CloudBlobStream); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, FunctionBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob to a CloudBlobStream.");
                }

                CloudBlobStream rawStream = await blockBlob.OpenWriteAsync(context.CancellationToken);
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.BlobWrittenWatcher);
                WatchableCloudBlobStream watchableStream = new WatchableCloudBlobStream(rawStream, committedAction);
                return new CloudBlobStreamValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class CloudBlobStreamValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public CloudBlobStreamValueBinder(ICloudBlob blob, WatchableCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(CloudBlobStream); }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return _stream;
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    // Not ByRef, so can ignore value argument.

                    // Determine whether or not to upload the blob.
                    if (await _stream.CompleteAsync(cancellationToken))
                    {
                        _stream.Dispose(); // Can only dispose when committing; see note on class above.
                    }
                }

                public string ToInvokeString()
                {
                    return _blob.GetBlobPath();
                }
            }
        }
    }
}

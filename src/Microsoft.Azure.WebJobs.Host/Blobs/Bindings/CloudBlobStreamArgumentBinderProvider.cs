// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class CloudBlobStreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

        public CloudBlobStreamArgumentBindingProvider(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
        {
            if (blobWrittenWatcherGetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherGetter");
            }

            _blobWrittenWatcherGetter = blobWrittenWatcherGetter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(CloudBlobStream))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind CloudBlobStream using access " + access.Value.ToString() + ".");
            }

            return new CloudBlobStreamArgumentBinding(_blobWrittenWatcherGetter);
        }

        private class CloudBlobStreamArgumentBinding : IBlobArgumentBinding
        {
            private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

            public CloudBlobStreamArgumentBinding(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
            {
                if (blobWrittenWatcherGetter == null)
                {
                    throw new ArgumentNullException("blobWrittenWatcherGetter");
                }

                _blobWrittenWatcherGetter = blobWrittenWatcherGetter;
            }

            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(CloudBlobStream); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob,
                    context, _blobWrittenWatcherGetter.Value);
                return new CloudBlobStreamValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class CloudBlobStreamValueBinder : IValueBinder, IWatchable
            {
                private readonly IStorageBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public CloudBlobStreamValueBinder(IStorageBlob blob, WatchableCloudBlobStream stream)
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
                    Debug.Assert(value == null || value == GetValue(), 
                        "The value argument should be either the same instance as returned by GetValue() or null");

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

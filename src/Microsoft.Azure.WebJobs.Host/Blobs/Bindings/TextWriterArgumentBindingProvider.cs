// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class TextWriterArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

        public TextWriterArgumentBindingProvider(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
        {
            if (blobWrittenWatcherGetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherGetter");
            }

            _blobWrittenWatcherGetter = blobWrittenWatcherGetter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(TextWriter))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind blob to TextWriter using access "
                    + access.Value.ToString() + ".");
            }

            return new TextWriterArgumentBinding(_blobWrittenWatcherGetter);
        }

        private class TextWriterArgumentBinding : IBlobArgumentBinding
        {
            private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

            public TextWriterArgumentBinding(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
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
                get { return typeof(TextWriter); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob,
                    context, _blobWrittenWatcherGetter.Value);
                const int DefaultBufferSize = 1024;

                TextWriter writer = new StreamWriter(watchableStream, Encoding.UTF8, DefaultBufferSize, leaveOpen: true);

                return new TextWriterValueBinder(blob, watchableStream, writer);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private class TextWriterValueBinder : IValueBinder, IWatchable
            {
                private readonly IStorageBlob _blob;
                private readonly WatchableCloudBlobStream _stream;
                private readonly TextWriter _value;

                public TextWriterValueBinder(IStorageBlob blob, WatchableCloudBlobStream stream, TextWriter value)
                {
                    _blob = blob;
                    _stream = stream;
                    _value = value;
                }

                public Type Type
                {
                    get { return typeof(TextWriter); }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public Task<object> GetValueAsync()
                {
                    return Task.FromResult<object>(_value);
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    Debug.Assert(value == null || value == await GetValueAsync(),
                        "The value argument should be either the same instance as returned by GetValueAsync() or null");

                    // Not ByRef, so can ignore value argument.
                    cancellationToken.ThrowIfCancellationRequested();
                    await _value.FlushAsync();
                    _value.Dispose();

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

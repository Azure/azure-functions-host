// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class OutByteArrayArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

        public OutByteArrayArgumentBindingProvider(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
        {
            if (blobWrittenWatcherGetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherGetter");
            }

            _blobWrittenWatcherGetter = blobWrittenWatcherGetter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(byte[]).MakeByRefType())
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Cannot bind blob out byte[] using access '{0}'.", access.Value.ToString()));
            }

            return new OutputByteArrayBinding(_blobWrittenWatcherGetter);
        }

        private class OutputByteArrayBinding : IBlobArgumentBinding
        {
            private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

            public OutputByteArrayBinding(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
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
                get { return typeof(byte[]); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob, context, _blobWrittenWatcherGetter.Value);
                return new ByteArrayValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class ByteArrayValueBinder : IValueBinder, IWatchable
            {
                private readonly IStorageBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public ByteArrayValueBinder(IStorageBlob blob, WatchableCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(byte[]); }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public Task<object> GetValueAsync()
                {
                    return Task.FromResult<object>(null);
                }

                /// <summary>
                /// Stores content of a byte[] into the bound CloudBLob.
                /// </summary>
                /// <param name="value">byte[] object as retrieved from user's WebJobs method argument.</param>
                /// <param name="cancellationToken">a cancellation token</param>
                /// <remarks>
                /// The out byte[] parameter is processed as follows:
                /// <list type="bullet">
                /// <item>
                /// <description>
                /// If the value is <see langword="null"/>, no blob will be created.
                /// </description>
                /// </item>
                /// <item>
                /// <description>
                /// If the value is an empty byte[], a blob with empty content will be created.
                /// </description>
                /// </item>
                /// <item>
                /// <description>
                /// If the value is a non-empty byte[], a blob with that content will be created.
                /// </description>
                /// </item>
                /// </list>
                /// </remarks>
                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    if (value == null)
                    {
                        return;
                    }

                    using (_stream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        byte[] data = (byte[])value;
                        await _stream.WriteAsync(data, 0, data.Length);
                        await _stream.CommitAsync(cancellationToken);
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

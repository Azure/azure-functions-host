// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class OutStringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

        public OutStringArgumentBindingProvider(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
        {
            if (blobWrittenWatcherGetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherGetter");
            }

            _blobWrittenWatcherGetter = blobWrittenWatcherGetter;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(string).MakeByRefType())
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Write)
            {
                throw new InvalidOperationException("Cannot bind blob out string using access "
                    + access.Value.ToString() + ".");
            }

            return new StringArgumentBinding(_blobWrittenWatcherGetter);
        }

        private class StringArgumentBinding : IBlobArgumentBinding
        {
            private readonly IContextGetter<IBlobWrittenWatcher> _blobWrittenWatcherGetter;

            public StringArgumentBinding(IContextGetter<IBlobWrittenWatcher> blobWrittenWatcherGetter)
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
                get { return typeof(string); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob, context,
                    _blobWrittenWatcherGetter.Value);
                return new StringValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class StringValueBinder : IValueBinder, IWatchable
            {
                private readonly IStorageBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public StringValueBinder(IStorageBlob blob, WatchableCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(string); }
                }

                public IWatcher Watcher
                {
                    get { return _stream; }
                }

                public object GetValue()
                {
                    return null;
                }

                /// <summary>
                /// Stores content of a string in utf-8 encoded format into the bound CloudBLob.
                /// </summary>
                /// <param name="value">string object as retrieved from user's WebJobs method argument.</param>
                /// <param name="cancellationToken">a cancellation token</param>
                /// <remarks>
                /// The out string parameter is processed as follows:
                /// <list type="bullet">
                /// <item>
                /// <description>
                /// If the value is <see langword="null"/>, no blob will be created.
                /// </description>
                /// </item>
                /// <item>
                /// <description>
                /// If the value is an empty string, a blob with empty content will be created.
                /// </description>
                /// </item>
                /// <item>
                /// <description>
                /// If the value is a non-empty string, a blob with that content will be created.
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

                    string text = (string)value;

                    const int defaultBufferSize = 1024;

                    using (_stream)
                    {
                        using (TextWriter writer = new StreamWriter(_stream, Encoding.UTF8, defaultBufferSize,
                            leaveOpen: true))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await writer.WriteAsync(text);
                        }

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

// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Bindings
{
    internal class TextWriterArgumentBindingProvider : IBlobArgumentBindingProvider
    {
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

            return new TextWriterArgumentBinding();
        }

        private class TextWriterArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(TextWriter); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob,
                    context);
                const int defaultBufferSize = 1024;
                TextWriter writer = new StreamWriter(watchableStream, Encoding.UTF8, defaultBufferSize,
                    leaveOpen: true);
                return new TextWriterValueBinder(blob, watchableStream, writer);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private class TextWriterValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly WatchableCloudBlobStream _stream;
                private readonly TextWriter _value;

                public TextWriterValueBinder(ICloudBlob blob, WatchableCloudBlobStream stream, TextWriter value)
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

                public object GetValue()
                {
                    return _value;
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    Debug.Assert(value == null || value == GetValue(),
                        "The value argument should be either the same instance as returned by GetValue() or null");

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

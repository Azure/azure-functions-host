// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal class OutStringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
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

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(string); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                CloudBlockBlob blockBlob = blob as CloudBlockBlob;

                if (blockBlob == null)
                {
                    throw new InvalidOperationException("Cannot bind a page blob using an out string.");
                }

                CloudBlobStream rawStream = await blockBlob.OpenWriteAsync(context.CancellationToken);
                IBlobCommitedAction committedAction = new BlobCommittedAction(blob, context.FunctionInstanceId,
                    context.BlobWrittenWatcher);
                WatchableCloudBlobStream watchableStream = new WatchableCloudBlobStream(rawStream, committedAction);
                return new StringValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class StringValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public StringValueBinder(ICloudBlob blob, WatchableCloudBlobStream stream)
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

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
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

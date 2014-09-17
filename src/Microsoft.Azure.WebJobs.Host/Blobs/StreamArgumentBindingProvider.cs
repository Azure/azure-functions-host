// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class StreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(Stream))
            {
                return null;
            }

            if (!access.HasValue)
            {
                throw new InvalidOperationException(String.Format(
                    "FileAccess must be specified when binding the parameter '{0}' to a blob Stream. " + 
                    "Add a FileAccess argument to the BlobAttribute constructor " + 
                    @"(for example, [Blob(""..."", FileAccess.Read)]).", 
                    parameter.Name));
            }

            switch(access.Value)
            {
                case FileAccess.ReadWrite:
                    throw new InvalidOperationException("Cannot bind blob to Stream using access ReadWrite.");
                case FileAccess.Read:
                    return new ReadStreamArgumentBinding();
                case FileAccess.Write:
                default:
                    return new WriteStreamArgumentBinding();
            }
        }

        private class ReadStreamArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(Stream); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                WatchableReadStream watchableStream = await ReadBlobArgumentBinding.BindStreamAsync(blob, context);
                return new BlobWatchableDisposableValueProvider(blob, watchableStream, typeof(Stream),
                    watcher: watchableStream, disposable: watchableStream);
            }
        }

        private class WriteStreamArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Write; }
            }

            public Type ValueType
            {
                get { return typeof(Stream); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                WatchableCloudBlobStream watchableStream = await WriteBlobArgumentBinding.BindStreamAsync(blob,
                    context);
                return new WriteStreamValueBinder(blob, watchableStream);
            }

            // There's no way to dispose a CloudBlobStream without committing.
            // This class intentionally does not implement IDisposable because there's nothing it can do in Dispose.
            private sealed class WriteStreamValueBinder : IValueBinder, IWatchable
            {
                private readonly ICloudBlob _blob;
                private readonly WatchableCloudBlobStream _stream;

                public WriteStreamValueBinder(ICloudBlob blob, WatchableCloudBlobStream stream)
                {
                    _blob = blob;
                    _stream = stream;
                }

                public Type Type
                {
                    get { return typeof(Stream); }
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

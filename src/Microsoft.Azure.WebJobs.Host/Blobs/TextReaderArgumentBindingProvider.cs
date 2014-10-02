// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class TextReaderArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(TextReader))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to TextReader using access "
                    + access.Value.ToString() + ".");
            }

            return new TextReaderArgumentBinding();
        }

        private class TextReaderArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(TextReader); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableReadStream watchableStream = await ReadBlobArgumentBinding.TryBindStreamAsync(blob, context);
                if (watchableStream == null)
                {
                    return BlobValueProvider.CreateWithNull<TextReader>(blob);
                }

                TextReader reader = ReadBlobArgumentBinding.CreateTextReader(watchableStream);
                return new BlobWatchableDisposableValueProvider(blob, reader, typeof(TextReader),
                    watcher: watchableStream, disposable: reader);
            }
        }
    }
}

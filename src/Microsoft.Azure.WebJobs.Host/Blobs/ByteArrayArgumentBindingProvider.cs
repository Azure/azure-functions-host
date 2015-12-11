// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class ByteArrayArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(byte[]))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Cannot bind blob to byte[] using access '{0}'.", access.Value.ToString()));
            }

            return new ByteArrayArgumentBinding();
        }

        private class ByteArrayArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(byte[]); }
            }

            public async Task<IValueProvider> BindAsync(IStorageBlob blob, ValueBindingContext context)
            {
                WatchableReadStream watchableStream = await ReadBlobArgumentBinding.TryBindStreamAsync(blob, context);
                if (watchableStream == null)
                {
                    return BlobValueProvider.CreateWithNull<byte[]>(blob);
                }

                byte[] value;
                ParameterLog status;

                using (watchableStream)
                using (MemoryStream outputStream = new MemoryStream())
                {
                    const int DefaultBufferSize = 4096;
                    await watchableStream.CopyToAsync(outputStream, DefaultBufferSize);
                    value = outputStream.ToArray();
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, typeof(byte[]), new ImmutableWatcher(status));
            }
        }
    }
}

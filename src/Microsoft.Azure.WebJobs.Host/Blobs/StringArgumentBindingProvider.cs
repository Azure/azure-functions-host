// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class StringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(string))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to string using access "
                    + access.Value.ToString() + ".");
            }

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(string); }
            }

            public async Task<IValueProvider> BindAsync(ICloudBlob blob, ValueBindingContext context)
            {
                string value;
                ParameterLog status;

                using (WatchableReadStream watchableStream = await ReadBlobArgumentBinding.BindStreamAsync(blob,
                    context))
                using (TextReader reader = ReadBlobArgumentBinding.CreateTextReader(watchableStream))
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    value = await reader.ReadToEndAsync();
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, typeof(string), new ImmutableWatcher(status));
            }
        }
    }
}

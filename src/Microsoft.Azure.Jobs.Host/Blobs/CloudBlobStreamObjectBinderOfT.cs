// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobStreamObjectBinder<TValue> : ICloudBlobStreamObjectBinder
    {
        private readonly ICloudBlobStreamBinder<TValue> _innerBinder;

        public CloudBlobStreamObjectBinder(ICloudBlobStreamBinder<TValue> innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public async Task<object> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken)
        {
            return await _innerBinder.ReadFromStreamAsync(input, cancellationToken);
        }

        public Task WriteToStreamAsync(Stream output, object value, CancellationToken cancellationToken)
        {
            return _innerBinder.WriteToStreamAsync((TValue)value, output, cancellationToken);
        }
    }
}

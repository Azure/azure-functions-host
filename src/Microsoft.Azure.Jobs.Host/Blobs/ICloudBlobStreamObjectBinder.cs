// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal interface ICloudBlobStreamObjectBinder
    {
        Task<object> ReadFromStreamAsync(Stream input, CancellationToken cancellationToken);

        Task WriteToStreamAsync(Stream output, object value, CancellationToken cancellationToken);
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Bindings;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal interface IBindableBlobPath : IBindablePath<BlobPath>
    {
        string ContainerNamePattern { get; }
        string BlobNamePattern { get; }
    }
}

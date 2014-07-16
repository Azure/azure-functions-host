// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    interface IBindableBlobPath : IBindablePath<BlobPath>
    {
        string ContainerNamePattern { get; }
        string BlobNamePattern { get; }
    }
}

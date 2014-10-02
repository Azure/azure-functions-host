// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Defines a page blob.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public interface IStoragePageBlob : IStorageBlob
#else
    internal interface IStoragePageBlob : IStorageBlob
#endif
    {
        /// <summary>Gets the underlying <see cref="CloudPageBlob"/>.</summary>
        new CloudPageBlob SdkObject { get; }
    }
}

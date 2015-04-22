// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    interface IBlobPathSource
    {
        string ContainerNamePattern { get; }

        string BlobNamePattern { get; }

        IEnumerable<string> ParameterNames { get; }

        IReadOnlyDictionary<string, object> CreateBindingData(BlobPath actualBlobPath);

        string ToString();
    }
}

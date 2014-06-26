using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal interface IBlobTimestampReader
    {
        DateTime? GetLastModifiedTimestamp(ICloudBlob blob);
    }
}

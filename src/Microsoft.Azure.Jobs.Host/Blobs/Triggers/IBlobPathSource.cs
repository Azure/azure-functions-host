using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
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

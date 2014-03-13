using System;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal interface IBlobListener
    {
        // Scan the container
        // Callbacks may fire multiple times. Or out of order relative to creation date. 
        void Poll(Action<CloudBlob, CancellationToken> callback, CancellationToken cancel);
    }
}

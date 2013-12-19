using System;
using System.Threading;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class BlobListenerExtensions
    {
        public static void Poll(this IBlobListener p, Action<CloudBlob> callback)
        {
            p.Poll(callback, CancellationToken.None);
        }
    }
}

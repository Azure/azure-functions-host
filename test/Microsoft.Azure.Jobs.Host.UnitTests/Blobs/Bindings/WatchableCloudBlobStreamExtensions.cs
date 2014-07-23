using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;

namespace Microsoft.Azure.Jobs.Host.UnitTests.Blobs.Bindings
{
    internal static class WatchableCloudBlobStreamExtensions
    {
        public static bool Complete(this WatchableCloudBlobStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            return stream.CompleteAsync(CancellationToken.None).Result;
        }
    }
}

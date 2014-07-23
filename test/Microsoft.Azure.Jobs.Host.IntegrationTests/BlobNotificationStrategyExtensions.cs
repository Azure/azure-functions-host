using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Blobs.Listeners;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    internal static class BlobNotificationStrategyExtensions
    {
        public static void Execute(this IBlobNotificationStrategy strategy)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException("strategy");
            }

            strategy.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}

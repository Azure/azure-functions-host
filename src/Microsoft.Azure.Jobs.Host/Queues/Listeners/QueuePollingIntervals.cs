using System;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal static class QueuePollingIntervals
    {
        public static readonly TimeSpan Minimum = new TimeSpan(0, 0, 2);
        public static readonly TimeSpan Maximum = new TimeSpan(0, 10, 0);
    }
}

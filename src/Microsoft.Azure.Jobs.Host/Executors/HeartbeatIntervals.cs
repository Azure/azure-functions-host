using System;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal static class HeartbeatIntervals
    {
        public static readonly TimeSpan NormalSignalInterval = new TimeSpan(0, 0, 30);
        public static readonly TimeSpan MinimumSignalInterval = new TimeSpan(0, 0, 10);
        public static readonly TimeSpan ExpirationInterval = new TimeSpan(0, 0, 45);
    }
}

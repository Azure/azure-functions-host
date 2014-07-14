using System;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal static class FunctionOutputIntervals
    {
        public static readonly TimeSpan InitialDelay = TimeSpan.Zero;
        public static readonly TimeSpan RefreshRate = new TimeSpan(0, 1, 0);
    }
}

using System;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal static class FunctionParameterLogIntervals
    {
        public static readonly TimeSpan InitialDelay = new TimeSpan(0, 0, 3); // Wait before first Log, small for initial quick log
        public static readonly TimeSpan RefreshRate = new TimeSpan(0, 0, 10); // Between log calls
    }
}

using System;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    internal class Utility
    {
        private static readonly DateTime EpochStartTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime TrimSubMilliseconds(DateTime dateTime)
        {
            if (dateTime <= EpochStartTime)
            {
                return dateTime;
            }

            var epochTotalMillisecondsDouble = (dateTime - EpochStartTime).TotalMilliseconds;
            var epochTotalMilliseconds = (long)Math.Round(epochTotalMillisecondsDouble, MidpointRounding.AwayFromZero);
            return EpochStartTime.AddMilliseconds(epochTotalMilliseconds);
        }
    }
}

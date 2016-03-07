// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Logging
{
    // A "time bucket" is a discrete unit of time that's useful for aggregation and reporting. 
    // Use minutes since a baseline.  
    static class TimeBucket
    {
        static DateTime _baselineTime = new DateTime(2000, 1, 1);

        public static DateTime ConveretToDateTime(long bucket)
        {
            return _baselineTime.AddMinutes(bucket);
        }

        public static long ConvertToBucket(DateTime dt)
        {
            TimeSpan ts = dt - _baselineTime;

            var min = (long)ts.TotalMinutes;
            if (min < 0)
            {
                return 0;
            }
            return min;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    // A "time bucket" is a discrete unit of time that's useful for aggregation and reporting. 
    // Use minutes since a baseline.  
    internal static class TimeBucket
    {
        static DateTime _baselineTime = new DateTime(2000, 1, 1);

        public static DateTime ConvertToDateTime(long bucket)
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

        public static DateTime CommonEpoch = new DateTime(0, DateTimeKind.Utc);
        public static string CommonEpochSuffix = "common";

        // The 'epoch' is a mapping from DateTime onto integers that preserves ordering. 
        // For example, if we have a table query between Start and End datetimes, we need to determine 
        // the subset of tables that can possible hold that date range. 
        // Use integers instead of datetime because:
        // - It provides a sortable view of the date ranges. 
        // - integers are valid Azure Table characters, so they can easily be a table suffix. 
        public static long GetEpochNumberFromTable(CloudTable table)
        {
            string tableName = table.Name;
            if (tableName.Length < 5)
            {
                return -1;
            }
            string suffix = tableName.Substring(tableName.Length - 6, 6);
            long epoch;
            if (long.TryParse(suffix, out epoch))
            {
                return epoch;
            }
            return -1;
        }

        // Epoch must be positive, orderd integers.
        // Use YYYYMM 
        private static long GetEpochSuffixNumber(DateTime epoch)
        {
            var year = epoch.Year; // 4 digit
            var month = epoch.Month; // 1..12

            int i = year * 100 + month;
            return i;
        }

        public static CloudTable GetTableForDateTime(this ILogTableProvider tableLookup, DateTime epoch)
        {
            // Epoch(DateTime.MaxValue) is 94146, still a 5 digit number. 
            string suffix;
            if (epoch == CommonEpoch)
            {
                suffix = CommonEpochSuffix;
            }
            else
            {
                var ts = GetEpochSuffixNumber(epoch);
                suffix = string.Format(CultureInfo.InvariantCulture, "{0:D5}", ts);
            }
            var table = tableLookup.GetTable(suffix);
            return table;
        }

        public static CloudTable GetTableForTimeBucket(this ILogTableProvider tableLookup, long timeBucket)
        {
            var time = ConvertToDateTime(timeBucket);
            return tableLookup.GetTableForDateTime(time);
        }

    }
}

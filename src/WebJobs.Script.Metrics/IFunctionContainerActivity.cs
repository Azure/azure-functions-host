using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public interface IFunctionContainerActivity
    {
        string SiteName { get; set; }
        /// <summary>
        /// Execution units in MB-ms
        /// </summary>
        long FunctionExecutionUnits { get; set; }
        int LastMemoryBucketSize { get; set; }
        long FunctionExecutionCount { get; set; }
        long FunctionExecutionTimeInMs { get; set; }
        DateTime LastFunctionExecutionCalcuationMemorySnapshotTime { get; set; }
        DateTime LastFunctionUpdateTimeStamp { get; set; }
        int FunctionContainerSizeInMb { get; set; }
        IDictionary<string, IFunctionActivity> ActiveFunctionActivities { get; set; }

        SortedList<DateTime, double> MemorySnapshots { get; set; }
    }
}

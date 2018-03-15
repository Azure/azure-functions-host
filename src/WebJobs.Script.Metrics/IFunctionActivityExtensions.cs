using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public static class IFunctionActivityExtensions
    {
        internal const string TimeFormatForLogging = "HH:mm:ss.fff";

        public static long GetFunctionExecutionUnits(this IFunctionActivity functionActivity)
        {
            if (functionActivity.DynamicMemoryBucketCalculations.Count == 0)
            {
                return 0;
            }
            else
            {
                return functionActivity.DynamicMemoryBucketCalculations.Sum(c => c.FunctionExecutionUnits);
            }
        }

        public static long GetTotalDynamicMemoryBilledTime(this IFunctionActivity functionActivity)
        {
            if (functionActivity.DynamicMemoryBucketCalculations.Count == 0)
            {
                return 0;
            }
            else
            {
                return functionActivity.DynamicMemoryBucketCalculations.Sum(c => c.FunctionTimeSlice);
            }
        }

        public static string SerializeDynamicMemoryBucketCalculations(this IFunctionActivity functionActivity)
        {
            const string TruncationMarker = "Truncated";
            const int MaxSerializedDynamicMemoryBucketCalculationsLength = 1280;

            var stringBuffer = new StringBuilder(MaxSerializedDynamicMemoryBucketCalculationsLength);
            var stringBufferLocal = new StringBuilder(MaxSerializedDynamicMemoryBucketCalculationsLength);

            for (var currentIndex = 0; currentIndex < functionActivity.DynamicMemoryBucketCalculations.Count && currentIndex < 100; currentIndex++)
            {
                var bucketCalculation = functionActivity.DynamicMemoryBucketCalculations[currentIndex];
                stringBufferLocal.Clear();
                if (currentIndex != 0)
                {
                    // End of line marker
                    stringBufferLocal.Append("EOL");
                }
                // Let's use acronyms to reduce the length of serialized string
                // MSST: Memory snapshot start time
                // MSET: Memory snapshot end time
                // FSST: Function Slice start time
                // FSET: Function Slice start time
                // TM: Total Memory
                // FEU: Function Execution Units
                // FTS: Function time slice
                stringBufferLocal.AppendFormat("MSST:{0} MSET:{1} FSST:{2} FSET:{3} TM:{4} FEU:{5} FTS:{6}",
                    bucketCalculation.MemorySnapshotStartTime.ToString(TimeFormatForLogging),
                    bucketCalculation.MemorySnapshotEndTime.ToString(TimeFormatForLogging),
                    bucketCalculation.FunctionSliceStartTime.ToString(TimeFormatForLogging),
                    bucketCalculation.FunctionSliceEndTime.ToString(TimeFormatForLogging),
                    bucketCalculation.TotalMemory,
                    bucketCalculation.FunctionExecutionUnits,
                    bucketCalculation.FunctionTimeSlice);

                if ((stringBuffer.Length + stringBufferLocal.Length + TruncationMarker.Length) < MaxSerializedDynamicMemoryBucketCalculationsLength)
                {
                    stringBuffer.Append(stringBufferLocal.ToString());
                }
                else
                {
                    stringBuffer.Append(TruncationMarker);
                    break;
                }
            }

            return stringBuffer.ToString();
        }

        public static string FormatFunctionDetails(this IFunctionActivity functionActivity)
        {
            return string.Format("ProcessId:{0} ExecutionId:{1} FunctionName: {2} InvocationId: {3} CurrentExecutionStage: {4} ExecutionTimeSpanInMs: {5} ActualExecutionTimeSpanInMs: {6} Concurrency: {7} IsSucceeded: {8} LastUpdatedTimeUtc: {9} StartTime:{10}",
                functionActivity.ProcessId,
                functionActivity.ExecutionId,
                functionActivity.FunctionName,
                functionActivity.InvocationId,
                functionActivity.CurrentExecutionStage,
                functionActivity.ExecutionTimeSpanInMs,
                functionActivity.ActualExecutionTimeSpanInMs,
                functionActivity.Concurrency,
                functionActivity.IsSucceeded,
                functionActivity.LastUpdatedTimeUtc,
                functionActivity.StartTime);
        }
    }
}

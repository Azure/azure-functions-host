using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    public interface IFunctionActivity
    {
        int ProcessId { get; set; }
        string ExecutionId { get; set; }
        string FunctionName { get; set; }
        string InvocationId { get; set; }
        DateTime StartTime { get; set; }
        DateTime EndTime { get; }
        DateTime LastUpdatedTimeUtc { get; set; }
        long ExecutionTimeSpanInMs { get; set; }
        // TODO: remove this "Actual" property?
        long ActualExecutionTimeSpanInMs { get; set; }
        FunctionExecutionStage CurrentExecutionStage { get; set; }
        int Concurrency { get; set; }
        bool IsSucceeded { get; set; }

        List<DynamicMemoryBucketCalculation> DynamicMemoryBucketCalculations { get; set; }
        object Clone();
        void CopyFrom(IFunctionActivity other);
    }

    public class DynamicMemoryBucketCalculation
    {
        public DateTime MemorySnapshotStartTime { get; set; }
        public DateTime MemorySnapshotEndTime { get; set; }
        public DateTime FunctionSliceStartTime { get; set; }
        public DateTime FunctionSliceEndTime { get; set; }
        public long FunctionTimeSlice { get; set; }
        public double TotalMemory { get; set; }
        /// <summary>
        /// Execution units in MB-ms
        /// </summary>
        public long FunctionExecutionUnits { get; set; }
    }

    [DataContract]
    public enum FunctionExecutionStage
    {
        /// <summary>
        /// The function is currently executing.
        /// </summary>
        [EnumMember]
        InProgress,

        /// <summary>
        /// The function has finished executing.
        /// </summary>
        [EnumMember]
        Finished
    }
}

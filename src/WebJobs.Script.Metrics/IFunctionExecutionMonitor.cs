using System;

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    /// <summary>
    /// Interface for monitoring function executions.
    /// </summary>
    public interface IFunctionExecutionMonitor
    {
        /// <summary>
        /// Method called when the status of a function execution has changed.
        /// </summary>
        /// <param name="status">The current execution status.</param>
        void FunctionExecution(FunctionExecutionStatus status);
    }

    public class FunctionExecutionStatus
    {
        public int ProcessId { get; set; }

        public string SiteName { get; set; }

        public string ExecutionId { get; set; }

        public int Concurrency { get; set; }

        public string FunctionName { get; set; }

        public string InvocationId { get; set; }

        public FunctionExecutionStage CurrentExecutionStage { get; set; }

        public long ExecutionTimeSpanInMs { get; set; }

        public bool IsSucceeded { get; set; }

        public DateTime StartTime { get; set; }
    }
}

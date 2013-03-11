using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Executor
{
    // Like  FunctionInstance plus output stats?
    // Provide the function instance for reply. 
    // Get list of inputs for logging. 
    // Times should all be in UTC.
    [DataServiceKey("PartitionKey", "RowKey")]
    public class ExecutionInstanceLogEntity : TableServiceEntity
    {
        public override string ToString()
        {
            var name = this.FunctionInstance.Location.MethodName;
            return string.Format("{0} @ {1}", name, this.QueueTime.Value.ToUniversalTime());
        }

        // rowKey = FunctionInstance.Guid?  uniquely identify the instance. 
        // Instance provides both the RowKey, as well as the invocation request information (like args)
        private FunctionInvokeRequest _functionInstance;

        public FunctionInvokeRequest FunctionInstance
        {
            get
            {
                return this._functionInstance;
            }
            set
            {
                this._functionInstance = value;

                if (value != null)
                {
                    // Update row and partition key, since those are based on the FunctionInstance
                    this.PartitionKey = "1";
                    this.RowKey = value.Id.ToString();
                }
            }
        }

        // If function threw an exception, set to type.fullname of that exception.
        // It's important that this is top-level and queryable because it's a key scenario to find failures. 
        // Provides a quick way to know if function failed. Get exception details from the logging. 
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }

        // For retrieving the console output.
        // Likely URL to a blob that the Console output was written to.
        public string OutputUrl { get; set; }

        // Set to once we've been queued. This should always been set since queuing is essentially initialization.
        // QueueTime is set on the machine that enqueues the request, which can be a different machine than 
        // the execution node. So there could be clocksqew between queue time and start time.        
        public DateTime? QueueTime { get; set; }

        // Set once we start to execute. null if only queued. 
        public DateTime? StartTime { get; set; }

        // Time that the job was completed (approximately when the user's code has finished running). 
        // Null if job is not yet complete.
        // To avoid clocksqew,  start and end time should be set by the same execution node. 
        // Set StartTime before the user code (including bindings) starts to run, set EndTime after it finishes. 
        public DateTime? EndTime { get; set; }

        // String that serves as a backpointer to the execution substrate.
        // Eg, if this was runas an azure task, this string can retrieve an azure task.
        public string Backpointer { get; set; }
    }

    public enum FunctionInstanceStatus
    {
        None, // shouldn't be used. Can indicate a serialization error.
        Queued, // waiting in the execution queue.
        Running, // Now running. An execution node has picked up ownership.
        CompletedSuccess, // ran to completion, either via success or a user error (threw exception)
        CompletedFailed, // ran to completion, but function through an exception before finishing
    }

    // Move to extension methods so that serializers don't pick them up. 
    public static class ExecutionInstanceLogEntityExtensions
    {
        // null if job hasn't finished yet.
        public static TimeSpan? GetDuration(this ExecutionInstanceLogEntity obj)
        {
            if (obj.EndTime.HasValue && obj.StartTime.HasValue)
            {
                return obj.EndTime.Value - obj.StartTime.Value;
            }
            return null;
        }

        // GEt a short summary string describing the queued, in-progress, completed info.
        public static string GetRunStatusString(this ExecutionInstanceLogEntity obj)
        {
            switch (obj.GetStatus())
            {
                case FunctionInstanceStatus.Queued:
                    return string.Format("In queued (since {0})", obj.QueueTime);
                case FunctionInstanceStatus.Running:
                    TimeSpan span = DateTime.UtcNow - obj.StartTime.Value;
                    return string.Format("Currently In-progress (for {0})", span);
                case FunctionInstanceStatus.CompletedSuccess:
                    return string.Format("Completed (at {0}, duration={1})", obj.EndTime, obj.GetDuration());
                case FunctionInstanceStatus.CompletedFailed:
                    return string.Format("Failed with exception: {1} (duration={0})", obj.GetDuration(), obj.ExceptionMessage);
                default:
                    return "???";
            }
        }

        public static FunctionInstanceStatus GetStatus(this ExecutionInstanceLogEntity obj)
        {
            if (obj.EndTime.HasValue)
            {
                if (obj.ExceptionType == null)
                {
                    return FunctionInstanceStatus.CompletedSuccess;
                }
                else
                {
                    return FunctionInstanceStatus.CompletedFailed;
                }
            }
            if (obj.StartTime.HasValue)
            {
                return FunctionInstanceStatus.Running;
            }
            return FunctionInstanceStatus.Queued;
        }

        public static bool IsCompleted(this ExecutionInstanceLogEntity obj)
        {
            var status = obj.GetStatus();
            return status == FunctionInstanceStatus.CompletedSuccess || status == FunctionInstanceStatus.CompletedFailed;
        }
    }
}
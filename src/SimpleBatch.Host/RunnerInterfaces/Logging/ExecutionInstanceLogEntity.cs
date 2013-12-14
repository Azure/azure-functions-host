using System;


namespace Microsoft.WindowsAzure.Jobs
{
    // Like  FunctionInstance plus output stats?
    // Provide the function instance for reply. 
    // Get list of inputs for logging. 
    // Times should all be in UTC.
    // This object gets mutated as the function executes. 
    internal class ExecutionInstanceLogEntity
    {
        public override string ToString()
        {
            var name = this.FunctionInstance.Location.GetShortName();
            string queueTime = this.QueueTime.HasValue ? 
                this.QueueTime.Value.ToUniversalTime().ToString() :
                "(awaiting prereqs)";
            return string.Format("{0} @ {1}", name, queueTime);
        }

        // This maps to the builtin property on azure Tables, so it will get set for us. 
        // This is the last time the object was updated. 
        public DateTime Timestamp { get; set; }

        // rowKey = FunctionInstance.Guid?  uniquely identify the instance. 
        // Instance provides both the RowKey, as well as the invocation request information (like args)
        public FunctionInvokeRequest FunctionInstance { get; set; }
        
        // If function threw an exception, set to type.fullname of that exception.
        // It's important that this is top-level and queryable because it's a key scenario to find failures. 
        // Provides a quick way to know if function failed. Get exception details from the logging. 
        public string ExceptionType { get; set; }
        public string ExceptionMessage { get; set; }

        // For retrieving the console output.
        // Likely URL to a blob that the Console output was written to.
        public string OutputUrl { get; set; }

        // Set to once we've been queued. 
        // This is not set if the function has outstanding prerequisites. 
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

        // Get a row key for azure tables
        // $$$ Should FunctionDefinition have this too? That one uses ToString(), and it's inconsistent.
        public string GetKey()
        {
            return this.FunctionInstance.Id.ToString();
        }
    }

    internal enum FunctionInstanceStatus
    {
        None, // shouldn't be used. Can indicate a serialization error.
        AwaitingPrereqs, // function is not yet queued. Has outstanding prereqs. 
        Queued, // waiting in the execution queue.
        Running, // Now running. An execution node has picked up ownership.
        CompletedSuccess, // ran to completion, either via success or a user error (threw exception)
        CompletedFailed, // ran to completion, but function through an exception before finishing
    }

    // Move to extension methods so that serializers don't pick them up. 
    internal static class ExecutionInstanceLogEntityExtensions
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
                case FunctionInstanceStatus.AwaitingPrereqs:
                    return string.Format("Awaiting prerequisites");
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

        // beware, object may be partially filled out. So bias checks to fields that are present rather than missing. 
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
            if (obj.QueueTime.HasValue)
            {
                // Queued, but not started or completed. 
                return FunctionInstanceStatus.Queued;    
            }

            // Not even queued. 
            return FunctionInstanceStatus.AwaitingPrereqs;            
        }

        public static bool IsCompleted(this ExecutionInstanceLogEntity obj)
        {
            var status = obj.GetStatus();
            return status.IsCompleted();
        }

        public static bool IsCompleted(this FunctionInstanceStatus status)
        {
            return status == FunctionInstanceStatus.CompletedSuccess || status == FunctionInstanceStatus.CompletedFailed;
        }
    }
}
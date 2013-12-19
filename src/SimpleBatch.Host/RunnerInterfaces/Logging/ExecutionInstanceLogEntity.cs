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

        // If a function is running after this time, its host process has ended; it never finished executing.
        public DateTime? HeartbeatExpires { get; set; }

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
}

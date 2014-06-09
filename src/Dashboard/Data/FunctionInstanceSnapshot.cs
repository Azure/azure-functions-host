using System;
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    public class FunctionInstanceSnapshot
    {
        public Guid Id { get; set; }

        public Guid HostInstanceId { get; set; }

        public string InstanceQueueName { get; set; }

        public HeartbeatDescriptor Heartbeat { get; set; }

        public string FunctionId { get; set; }

        public string FunctionFullName { get; set; }

        public string FunctionShortName { get; set; }

        public IDictionary<string, FunctionInstanceArgument> Arguments  { get; set; }

        public IDictionary<string, ParameterLog> ParameterLogs { get; set; }

        public Guid? ParentId  { get; set; }

        public string Reason { get; set; }

        public DateTimeOffset QueueTime { get; set; }

        public DateTimeOffset? StartTime { get; set; }

        public DateTimeOffset? EndTime { get; set; }

        public string StorageConnectionString { get; set; }

        public LocalBlobDescriptor OutputBlob { get; set; }

        public LocalBlobDescriptor ParameterLogBlob { get; set; }

        public string WebSiteName { get; set; }

        public string WebJobType { get; set; }

        public string WebJobName { get; set; }

        public string WebJobRunId { get; set; }

        public bool? Succeeded { get; set; }

        public string ExceptionType { get; set; }

        public string ExceptionMessage { get; set; }
    }
}

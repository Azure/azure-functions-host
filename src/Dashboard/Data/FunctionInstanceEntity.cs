using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace Dashboard.Data
{
    [CLSCompliant(false)]
    public class FunctionInstanceEntity : TableEntity
    {
        public Guid Id { get; set; }

        public Guid HostId { get; set; }

        public Guid HostInstanceId { get; set; }

        public string FunctionId { get; set; }

        public string FunctionFullName { get; set; }

        public string FunctionShortName { get; set; }

        public Guid? ParentId { get; set; }

        public string Reason { get; set; }

        public DateTimeOffset QueueTime { get; set; }

        public DateTimeOffset? StartTime { get; set; }

        public DateTimeOffset? EndTime { get; set; }

        public string StorageConnectionString { get; set; }

        public string OutputBlobUrl { get; set; }

        public string ParameterLogBlobUrl { get; set; }

        public string WebSiteName { get; set; }

        public string WebJobType { get; set; }

        public string WebJobName { get; set; }

        public string WebJobRunId { get; set; }

        public bool? Succeeded { get; set; }

        public string ExceptionType { get; set; }

        public string ExceptionMessage { get; set; }

        internal static string GetRowKey(Guid functionInstanceId)
        {
            return functionInstanceId.ToString();
        }
    }
}

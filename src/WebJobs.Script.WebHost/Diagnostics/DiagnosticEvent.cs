// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEvent : ITableEntity
    {
        internal const string CurrentEventVersion = "2024-05-01";

        public DiagnosticEvent() { }

        public DiagnosticEvent(string hostId, DateTime timestamp)
        {
            RowKey = TableStorageHelpers.GetRowKey(timestamp);
            PartitionKey = $"{hostId}-{timestamp:yyyyMMdd}";
            Timestamp = timestamp;
            EventVersion = CurrentEventVersion;
        }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public ETag ETag { get; set; }

        public string EventVersion { get; set; }

        public int HitCount { get; set; }

        public string Message { get; set; }

        public string ErrorCode { get; set; }

        public string HelpLink { get; set; }

        public int Level { get; set; }

        [IgnoreDataMember]
        public LogLevel LogLevel
        {
            get { return (LogLevel)Level; }
            set { Level = (int)value; }
        }

        public string Details { get; set; }
    }
}

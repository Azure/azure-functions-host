// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.WebJobs.Script.WebHost.Helpers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class DiagnosticEvent : TableEntity
    {
        public DiagnosticEvent() { }

        public DiagnosticEvent(string hostId, DateTime timestamp)
        {
            RowKey = TableStorageHelpers.GetRowKey(timestamp);
            PartitionKey = $"{hostId}-{timestamp:yyyyMMdd}";
            Timestamp = timestamp;
        }

        public int HitCount { get; set; }

        public string Message { get; set; }

        public string ErrorCode { get; set; }

        public string HelpLink { get; set; }

        public int Level { get; set; }

        [IgnoreProperty]
        public LogLevel LogLevel
        {
            get { return (LogLevel)Level; }
            set { Level = (int)value; }
        }

        public string Details { get; set; }
    }
}

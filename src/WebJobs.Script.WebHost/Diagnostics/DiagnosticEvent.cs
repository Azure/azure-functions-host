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
        public DiagnosticEvent(string hostId, DateTime now)
        {
            RowKey = TableStorageHelpers.GetRowKey(now);
            PartitionKey = $"{hostId}-{now:yyyyMMdd}";
        }

        public int HitCount { get; set; }

        public DateTime LastTimeStamp { get; set; }

        public string Message { get; set; }

        public string ErrorCode { get; set; }

        public string HelpLink { get; set; }

        public LogLevel Level { get; set; }

        public string Details { get; set; }
    }
}

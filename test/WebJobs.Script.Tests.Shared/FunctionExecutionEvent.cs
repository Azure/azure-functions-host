// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class FunctionExecutionEvent
    {
        public string ExecutionId { get; set; }

        public string SiteName { get; set; }

        public int Concurrency { get; set; }

        public string FunctionName { get; set; }

        public string InvocationId { get; set; }

        public ExecutionStage ExecutionStage { get; set; }

        public bool Success { get; set; }

        public long ExecutionTimeSpan { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public class FunctionActivity : ActivityBase
    {
        public string FunctionName { get; set; }

        public string InvocationId { get; set; }

        public int Concurrency { get; set; }

        public string ExecutionId { get; set; }

        public string ExecutionStage { get; set; }

        public bool IsSucceeded { get; set; }

        public long ExecutionTimeSpanInMs { get; set; }
    }
}

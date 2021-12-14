// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public enum ExecutionStage : byte
    {
        Started,
        InProgress,
        Finished,
        Failed,
        Succeeded
    }

    public readonly struct FunctionMetrics
    {
        public FunctionMetrics(string functionName, ExecutionStage executionStage, long executionTimeInMS)
        {
            FunctionName = functionName;
            ExecutionStage = executionStage;
            ExecutionTimeInMS = executionTimeInMS;
        }

        public string FunctionName { get; }

        public ExecutionStage ExecutionStage { get; }

        public long ExecutionTimeInMS { get; }
    }
}
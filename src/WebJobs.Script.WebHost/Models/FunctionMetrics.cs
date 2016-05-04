// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public enum ExecutionStage
    {
        Started,
        InProgress,
        Finished,
        Failed,
        Succeeded
    }

    public class FunctionMetrics
    {
        private string _functionName;
        private ExecutionStage _executionStage;
        private long _executionTimeInMS;

        public FunctionMetrics(string functionName, ExecutionStage executionStage, long executionTimeInMS)
        {
            _functionName = functionName;
            _executionStage = executionStage;
            _executionTimeInMS = executionTimeInMS;
        }

        public string FunctionName
        {
            get
            {
                return _functionName;
            }
        }

        public ExecutionStage ExecutionStage
        {
            get
            {
                return _executionStage;
            }
        }

        public long ExecutionTimeInMS
        {
            get
            {
                return _executionTimeInMS;
            }
        }
    }
}
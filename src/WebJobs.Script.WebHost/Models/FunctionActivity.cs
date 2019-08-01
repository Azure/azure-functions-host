// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public enum FunctionExecutionStage
    {
        /// <summary>
        /// The function is currently executing.
        /// </summary>
        [EnumMember]
        InProgress,

        /// <summary>
        /// The function has finished executing.
        /// </summary>
        [EnumMember]
        Finished
    }

    public class FunctionActivity : ActivityBase
    {
        public string FunctionName { get; set; }

        public string InvocationId { get; set; }

        public int Concurrency { get; set; }

        public string ExecutionId { get; set; }

        public FunctionExecutionStage ExecutionStage { get; set; }

        public bool IsSucceeded { get; set; }

        public long ExecutionTimeSpanInMs { get; set; }

        public DateTime StartTime { get; set; }
    }
}

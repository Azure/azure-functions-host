// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Metrics
{
    public interface IMetricsPublisher
    {
        void AddFunctionExecutionActivity(string functionName, string invocationId, int concurrency, string executionStage, bool success, long executionTimeSpan, string executionId, DateTime eventTimeStamp, DateTime functionStartTime);

        void OnFunctionStarted(string functionName, string invocationId);

        void OnFunctionCompleted(string functionName, string invocationId);

        /// <summary>
        /// Notifies the publisher that a function invocation completed.
        /// </summary>
        /// <param name="functionName">The function name.</param>
        /// <param name="invocationId">The invocation identifier.</param>
        /// <param name="success">Whether the invocation completed successfully.</param>
        /// <remarks>
        /// The default implementation ignores <paramref name="success"/> and forwards to the two-argument overload.
        /// Publishers that consume the completion outcome must override this method.
        /// </remarks>
        void OnFunctionCompleted(string functionName, string invocationId, bool success)
        {
            OnFunctionCompleted(functionName, invocationId);
        }
    }
}
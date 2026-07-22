// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ScriptTelemetryProcessor : ITelemetryProcessor
    {
        // Matches the Application Insights limit for the problemId field.
        private const int MaxProblemIdLength = 1024;

        internal static readonly AsyncLocal<bool> SuppressDependencyTelemetry = new();

        public ScriptTelemetryProcessor(ITelemetryProcessor next)
        {
            this.Next = next;
        }

        private ITelemetryProcessor Next { get; set; }

        public void Process(ITelemetry item)
        {
            // Filter out HTTP dependency telemetry originating from the host's proxy calls to
            // out-of-proc workers.
            if (item is DependencyTelemetry && SuppressDependencyTelemetry.Value)
            {
                return;
            }

            // Only process if exception is thrown by user code (if IsUserException is true).
            if (item is ExceptionTelemetry exceptionTelemetry
                && exceptionTelemetry?.Exception?.InnerException is RpcException rpcException
                && (rpcException?.IsUserException).GetValueOrDefault())
            {
                item = ToUserException(rpcException, exceptionTelemetry);
            }
            this.Next.Process(item);
        }

        private static ITelemetry ToUserException(RpcException rpcException, ExceptionTelemetry originalItem)
        {
            string typeName = string.IsNullOrEmpty(rpcException.RemoteTypeName) ? rpcException.GetType().ToString() : rpcException.RemoteTypeName;

            var userExceptionDetails = new ExceptionDetailsInfo(1, -1, typeName, rpcException.RemoteMessage, true, rpcException.RemoteStackTrace, new StackFrame[] { });

            // Compute a real problem id (type + top user frame) so these exceptions group like any
            // other exception in the portal instead of collapsing into one literal "ProblemId" bucket.
            ExceptionTelemetry newET = new ExceptionTelemetry(new[] { userExceptionDetails },
            originalItem.SeverityLevel ?? SeverityLevel.Error, ComputeProblemId(typeName, rpcException.RemoteStackTrace),
            originalItem.Properties,
            new Dictionary<string, double>() { });

            // This telemetry is created after initializers and (when configured) sampling have already
            // run on the original item, so anything not copied here is lost: carry over the correlation
            // and cloud context, and the sampling decision so weighted counts stay accurate.
            newET.Context.InstrumentationKey = originalItem.Context.InstrumentationKey;
            newET.Context.Operation.Id = originalItem.Context.Operation.Id;
            newET.Context.Operation.Name = originalItem.Context.Operation.Name;
            newET.Context.Operation.ParentId = originalItem.Context.Operation.ParentId;
            newET.Context.Operation.SyntheticSource = originalItem.Context.Operation.SyntheticSource;
            newET.Context.Cloud.RoleName = originalItem.Context.Cloud.RoleName;
            newET.Context.Cloud.RoleInstance = originalItem.Context.Cloud.RoleInstance;
            newET.Timestamp = originalItem.Timestamp;

            ((ISupportSampling)newET).SamplingPercentage = ((ISupportSampling)originalItem).SamplingPercentage;

            return newET;
        }

        private static string ComputeProblemId(string typeName, string stackTrace)
        {
            if (!string.IsNullOrEmpty(stackTrace))
            {
                foreach (string line in stackTrace.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string frame = trimmed.Substring(3);
                    int argumentListStart = frame.IndexOf('(');
                    if (argumentListStart > 0)
                    {
                        frame = frame.Substring(0, argumentListStart);
                    }

                    string problemId = $"{typeName} at {frame.Trim()}";
                    return problemId.Length <= MaxProblemIdLength ? problemId : problemId.Substring(0, MaxProblemIdLength);
                }
            }

            // No parsable frame (empty stack, or a non-.NET worker's stack format): the type name still
            // groups far better than a constant.
            return typeName;
        }
    }
}

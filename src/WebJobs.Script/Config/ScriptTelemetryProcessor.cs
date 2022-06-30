// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Config
{
    internal class ScriptTelemetryProcessor : ITelemetryProcessor
    {
        public ScriptTelemetryProcessor(ITelemetryProcessor next)
        {
            this.Next = next;
        }

        private ITelemetryProcessor Next { get; set; }

        public void Process(ITelemetry item)
        {
            // Only process if exception is thrown by user code (if IsUserException is true).
            if (item is ExceptionTelemetry exceptionTelemetry
                && exceptionTelemetry?.Exception?.InnerException is RpcException rpcException
                && (rpcException?.IsUserException).GetValueOrDefault())
            {
                item = ToUserException(rpcException, item);
            }
            this.Next.Process(item);
        }

        private ITelemetry ToUserException(RpcException rpcException, ITelemetry originalItem)
        {
            string typeName = string.IsNullOrEmpty(rpcException.RemoteTypeName) ? rpcException.GetType().ToString() : rpcException.RemoteTypeName;

            var userExceptionDetails = new ExceptionDetailsInfo(1, -1, typeName, rpcException.RemoteMessage, true, rpcException.RemoteStackTrace, new StackFrame[] { });

            ExceptionTelemetry newET = new ExceptionTelemetry(new[] { userExceptionDetails },
            SeverityLevel.Error, "ProblemId",
            new Dictionary<string, string>() { },
            new Dictionary<string, double>() { });

            newET.Context.InstrumentationKey = originalItem.Context.InstrumentationKey;
            newET.Timestamp = originalItem.Timestamp;

            return newET;
        }

        /// <summary>
        /// Returns true if the feature flag for surfacing user code exceptions was set by the worker,
        /// and false if not.
        /// </summary>
        /// <param name="ex">The <see cref="Exception"/> instance.</param>
        private bool EnableUserExceptionFeatureFlag(Exception ex)
        {
            try
            {
                string value = (string)ex.Data[RpcWorkerConstants.EnableUserCodeException];
                return bool.Parse(value);
            }
            catch
            {
                return false;
            }
        }
    }
}

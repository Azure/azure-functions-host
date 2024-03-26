// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class StatusResultExtensions
    {
        public static bool IsFailure(this StatusResult statusResult, bool enableUserCodeExceptionCapability, out Exception exception)
        {
            switch (statusResult.Status)
            {
                case StatusResult.Types.Status.Failure:
                    exception = GetRpcException(statusResult, enableUserCodeExceptionCapability);
                    return true;

                case StatusResult.Types.Status.Cancelled:
                    exception = new TaskCanceledException();
                    return true;

                default:
                    exception = null;
                    return false;
            }
        }

        public static bool IsFailure(this StatusResult statusResult, out Exception exception)
        {
            return IsFailure(statusResult, false, out exception);
        }

        /// <summary>
        /// This method is only hit on the invocation code path.
        /// enableUserCodeExceptionCapability = feature flag exposed as a capability that is set by the worker.
        /// </summary>
        public static bool IsInvocationSuccess(this StatusResult status)
        {
            return status.Status switch
            {
                StatusResult.Types.Status.Failure or StatusResult.Types.Status.Cancelled => false,
                _ => true,
            };
        }

        /// <summary>
        /// If the capability is enabled, surface additional exception properties
        /// so that they can be surfaced to app insights by the ScriptTelemetryProcessor.
        /// </summary>
        public static Workers.Rpc.RpcException GetRpcException(this StatusResult statusResult, bool enableUserCodeExceptionCapability = false)
        {
            var ex = statusResult?.Exception;
            var status = statusResult?.Status.ToString();
            if (ex != null)
            {
                if (enableUserCodeExceptionCapability)
                {
                    return new Workers.Rpc.RpcException(status, ex.Message, ex.StackTrace, ex.Type, ex.IsUserException);
                }

                return new Workers.Rpc.RpcException(status, ex.Message, ex.StackTrace);
            }
            return new Workers.Rpc.RpcException(status, string.Empty, string.Empty);
        }
    }
}
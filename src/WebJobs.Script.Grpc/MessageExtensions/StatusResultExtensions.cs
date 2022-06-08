﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal static class StatusResultExtensions
    {
        public static bool IsFailure(this StatusResult statusResult, out Exception exception)
        {
            switch (statusResult.Status)
            {
                case StatusResult.Types.Status.Failure:
                    exception = GetRpcException(statusResult);
                    return true;

                case StatusResult.Types.Status.Cancelled:
                    exception = new TaskCanceledException();
                    return true;

                default:
                    exception = null;
                    return false;
            }
        }

        /// <summary>
        /// This method is only hit on the invocation code path.
        /// </summary>
        public static bool IsInvocationSuccess<T>(this StatusResult status, TaskCompletionSource<T> tcs)
        {
            switch (status.Status)
            {
                case StatusResult.Types.Status.Failure:
                    var rpcException = GetRpcException(status);
                    tcs.SetException(rpcException);
                    return false;

                case StatusResult.Types.Status.Cancelled:
                    tcs.SetCanceled();
                    return false;

                default:
                    return true;
            }
        }

        public static Workers.Rpc.RpcException GetRpcException(StatusResult statusResult)
        {
            var ex = statusResult?.Exception;
            var status = statusResult?.Status.ToString();
            if (ex != null)
            {
                return new Workers.Rpc.RpcException(status, ex.Message, ex.StackTrace, ex.Type, ex.IsUserException);
            }
            return new Workers.Rpc.RpcException(status, string.Empty, string.Empty);
        }
    }
}

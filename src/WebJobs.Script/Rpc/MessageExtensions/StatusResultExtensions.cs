// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
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

        public static bool IsSuccess<T>(this StatusResult status, TaskCompletionSource<T> tcs)
        {
            switch (status.Status)
            {
                case StatusResult.Types.Status.Failure:
                    tcs.SetException(GetRpcException(status));
                    return false;

                case StatusResult.Types.Status.Cancelled:
                    tcs.SetCanceled();
                    return false;

                default:
                    return true;
            }
        }

        public static RpcException GetRpcException(StatusResult statusResult)
        {
            var ex = statusResult?.Exception;
            var status = statusResult?.Status.ToString();
            if (ex != null)
            {
                return new RpcException(status, ex.Message, ex.StackTrace);
            }
            return new RpcException(status, string.Empty, string.Empty);
        }
    }
}

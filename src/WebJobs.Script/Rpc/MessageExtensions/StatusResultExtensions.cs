// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal static class StatusResultExtensions
    {
        public static bool IsFailure(this StatusResult status, out Exception exception)
        {
            switch (status.Status)
            {
                case StatusResult.Types.Status.Failure:
                    var exc = status.Exception;
                    exception = new RpcException(status.Result, exc.Message, exc.StackTrace);
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
                    var exc = status.Exception;
                    tcs.SetException(new RpcException(status.Result, exc.Message, exc.StackTrace));
                    return false;

                case StatusResult.Types.Status.Cancelled:
                    tcs.SetCanceled();
                    return false;

                default:
                    return true;
            }
        }
    }
}

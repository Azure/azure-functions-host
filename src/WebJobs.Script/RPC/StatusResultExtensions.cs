// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class StatusResultExtensions
    {
        public static void VerifySuccess(this StatusResult status)
        {
            switch (status.Status)
            {
                case StatusResult.Types.Status.Failure:
                    var exc = status.Exception;
                    throw new RpcException(status.Result, exc.Message, exc.StackTrace);

                case StatusResult.Types.Status.Cancelled:
                    throw new TaskCanceledException();

                default:
                    break;
            }
        }
    }
}

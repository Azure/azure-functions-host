// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class RpcFactory : IRpcFactory
    {
        public IRpc CreateRpcClient(string rpcProvider)
        {
            switch (rpcProvider)
            {
                case RpcConstants.ZeroMQ:
                    return new ZeroMQ();
                case RpcConstants.GoogleRpc:
                    return new GoogleRpc();
                default:
                    throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture,
                        "The Rpc provider {0} is not supported.", rpcProvider));
            }
        }
    }
}

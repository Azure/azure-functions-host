// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public interface IRpcWorkerProcessFactory
    {
        IWorkerProcess Create(string workerId, string runtime, string scriptRootPath, RpcWorkerConfig rpcWorkerConfig);
    }
}

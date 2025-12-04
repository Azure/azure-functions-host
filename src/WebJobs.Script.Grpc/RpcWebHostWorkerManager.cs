// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Rpc;

internal class RpcWebHostWorkerManager : IWebHostWorkerManager
{
    private readonly IWebHostRpcWorkerChannelManager _webHostChannelManager;

    public RpcWebHostWorkerManager(IWebHostRpcWorkerChannelManager webHostChannelManager)
    {
        _webHostChannelManager = webHostChannelManager;
    }

    public Task SpecializeAsync()
    {
        return _webHostChannelManager.SpecializeAsync();
    }

    public Task WorkerWarmupAsync()
    {
        return _webHostChannelManager.WorkerWarmupAsync();
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public interface IWebHostRpcWorkerChannelManager
    {
        Task<IRpcWorkerChannel> InitializeChannelAsync(IEnumerable<RpcWorkerConfig> workerConfigs, string language);

        Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> GetChannels(string language);

        Task SpecializeAsync();

        Task<bool> ShutdownChannelIfExistsAsync(string language, string workerId, Exception workerException);

        Task ShutdownChannelsAsync();

        Task WorkerWarmupAsync();
    }
}

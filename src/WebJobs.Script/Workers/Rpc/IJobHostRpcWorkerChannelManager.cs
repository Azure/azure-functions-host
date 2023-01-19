// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal interface IJobHostRpcWorkerChannelManager
    {
        void AddChannel(IRpcWorkerChannel channel, string language);

        Task<bool> ShutdownChannelIfExistsAsync(string channelId, Exception workerException);

        void ShutdownChannels();

        IEnumerable<IRpcWorkerChannel> GetChannels(string language);

        IEnumerable<IRpcWorkerChannel> GetChannels();
    }
}

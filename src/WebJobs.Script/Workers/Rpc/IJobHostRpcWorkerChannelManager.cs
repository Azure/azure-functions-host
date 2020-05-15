// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal interface IJobHostRpcWorkerChannelManager
    {
        void AddChannel(IRpcWorkerChannel channel);

        Task<bool> ShutdownChannelIfExistsAsync(string channelId);

        void ShutdownChannels();

        IEnumerable<IRpcWorkerChannel> GetChannels();
    }
}

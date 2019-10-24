// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal interface IJobHostRpcWorkerChannelManager
    {
        void AddChannel(IRpcWorkerChannel channel);

        void DisposeAndRemoveChannel(IRpcWorkerChannel channel);

        void DisposeAndRemoveChannels();

        IEnumerable<IRpcWorkerChannel> GetChannels();
    }
}

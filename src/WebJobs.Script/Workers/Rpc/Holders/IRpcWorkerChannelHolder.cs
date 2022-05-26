// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Holders
{
    internal class IRpcWorkerChannelHolder
    {
        public IRpcWorkerChannelHolder(string channelId, IRpcWorkerChannel workerChannel)
        {
            ChannelId = channelId;
            WorkerChannel = workerChannel;
        }

        public string ChannelId { get; }

        public IRpcWorkerChannel WorkerChannel { get; }

        public override bool Equals(object obj)
        {
            if (obj is IRpcWorkerChannelHolder rpcWorkerChannelHolder)
            {
                return rpcWorkerChannelHolder.ChannelId == this.ChannelId;
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return ChannelId.GetHashCode();
        }
    }
}

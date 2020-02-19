// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class JobHostRpcWorkerChannelManager : IJobHostRpcWorkerChannelManager
    {
        private readonly ILogger _logger;
        private ConcurrentDictionary<string, IRpcWorkerChannel> _channels = new ConcurrentDictionary<string, IRpcWorkerChannel>();

        public JobHostRpcWorkerChannelManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<JobHostRpcWorkerChannelManager>();
        }

        public void AddChannel(IRpcWorkerChannel channel)
        {
            _channels.TryAdd(channel.Id, channel);
        }

        public void DisposeAndRemoveChannel(IRpcWorkerChannel channel)
        {
            DisposeAndRemoveChannel(channel, null);
        }

        public void DisposeAndRemoveChannel(IRpcWorkerChannel channel, Exception workerException)
        {
            if (_channels.TryRemove(channel.Id, out IRpcWorkerChannel removedChannel))
            {
                _logger.LogDebug("Disposing language worker channel with id:{workerId}", removedChannel.Id);
                removedChannel.TryFailExecutions(workerException);
                (removedChannel as IDisposable)?.Dispose();
            }
        }

        public void DisposeAndRemoveChannels()
        {
            foreach (string channelId in _channels.Keys)
            {
                if (_channels.TryRemove(channelId, out IRpcWorkerChannel removedChannel))
                {
                    _logger.LogDebug("Disposing language worker channel with id:{workerId}", removedChannel.Id);
                    (removedChannel as IDisposable)?.Dispose();
                }
            }
        }

        public IEnumerable<IRpcWorkerChannel> GetChannels()
        {
            return _channels.Values;
        }
    }
}

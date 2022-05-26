// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class JobHostRpcWorkerChannelManager : IJobHostRpcWorkerChannelManager
    {
        private readonly ILogger _logger;
        private ConcurrentDictionary<string, ConcurrentDictionary<string, IRpcWorkerChannel>> _channels = new ConcurrentDictionary<string, ConcurrentDictionary<string, IRpcWorkerChannel>>();

        public JobHostRpcWorkerChannelManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<JobHostRpcWorkerChannelManager>();
        }

        public void AddChannel(IRpcWorkerChannel channel, string language)
        {
            if (_channels.TryGetValue(language, out ConcurrentDictionary<string, IRpcWorkerChannel> channels))
            {
                channels.TryAdd(channel.Id, channel);
            }
            else
            {
                _channels.TryAdd(language, new ConcurrentDictionary<string, IRpcWorkerChannel>
                {
                    [channel.Id] = channel,
                });
            }
        }

        public Task<bool> ShutdownChannelIfExistsAsync(string channelId, Exception workerException)
        {
            foreach (string language in _channels.Keys)
            {
                if (_channels.TryGetValue(language, out ConcurrentDictionary<string, IRpcWorkerChannel> channels))
                {
                    if (channels.TryRemove(channelId, out IRpcWorkerChannel rpcChannel))
                    {
                        _logger.LogDebug("Disposing language worker channel with id:{workerId}", rpcChannel.Id);
                        rpcChannel.TryFailExecutions(workerException);
                        (rpcChannel as IDisposable)?.Dispose();
                        return Task.FromResult(true);
                    }
                }
            }
            return Task.FromResult(false);
        }

        public void ShutdownChannels()
        {
            foreach (string language in _channels.Keys)
            {
                if (_channels.TryRemove(language, out ConcurrentDictionary<string, IRpcWorkerChannel> channels))
                {
                    foreach (var rpcWorkerChannel in channels.Values)
                    {
                        if (channels.TryRemove(rpcWorkerChannel.Id, out IRpcWorkerChannel _))
                        {
                            _logger.LogDebug("Disposing language worker channel with id:{workerId}", rpcWorkerChannel.Id);
                            (rpcWorkerChannel as IDisposable)?.Dispose();
                        }
                    }
                }
            }
        }

        public IEnumerable<IRpcWorkerChannel> GetChannels(string language)
        {
            _channels.TryGetValue(language, out ConcurrentDictionary<string, IRpcWorkerChannel> channels);
            return channels?.Values;
        }

        public IEnumerable<IRpcWorkerChannel> GetChannels()
        {
            List<IRpcWorkerChannel> rpcWorkerChannels = new List<IRpcWorkerChannel>();
            foreach (string language in _channels.Keys)
            {
                _channels.TryGetValue(language, out ConcurrentDictionary<string, IRpcWorkerChannel> channels);
                rpcWorkerChannels.AddRange(channels.Values);
            }
            return rpcWorkerChannels;
        }
    }
}

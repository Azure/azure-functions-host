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
        private readonly object _getChannelLock = new object();
        private readonly ILogger _logger;
        private ConcurrentDictionary<string, IRpcWorkerChannelDictionary> _channels = new ConcurrentDictionary<string, IRpcWorkerChannelDictionary>(StringComparer.OrdinalIgnoreCase);

        public JobHostRpcWorkerChannelManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<JobHostRpcWorkerChannelManager>();
        }

        public void AddChannel(IRpcWorkerChannel channel, string language)
        {
            lock (_getChannelLock)
            {
                if (_channels.TryGetValue(language, out IRpcWorkerChannelDictionary channels))
                {
                    channels.TryAdd(channel.Id, channel);
                }
                else
                {
                    _channels.TryAdd(language, new IRpcWorkerChannelDictionary
                    {
                        [channel.Id] = channel,
                    });
                }
            }
        }

        public Task<bool> ShutdownChannelIfExistsAsync(string channelId, Exception workerException)
        {
            foreach (string language in _channels.Keys)
            {
                if (_channels.TryGetValue(language, out IRpcWorkerChannelDictionary channels))
                {
                    if (channels.TryRemove(channelId, out IRpcWorkerChannel rpcChannel))
                    {
                        string id = rpcChannel.Id;
                        _logger.LogDebug("Disposing language worker channel with id:{workerId}", id);
                        rpcChannel.TryFailExecutions(workerException);

                        (rpcChannel as IDisposable)?.Dispose();
                        _logger.LogDebug("Disposed language worker channel with id:{workerId}", id);

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
                if (_channels.TryRemove(language, out IRpcWorkerChannelDictionary removedChannels))
                {
                    foreach (var removedChannel in removedChannels.Values)
                    {
                        if (removedChannels.TryRemove(removedChannel.Id, out IRpcWorkerChannel _))
                        {
                            _logger.LogDebug("Disposing language worker channel with id:{workerId}", removedChannel.Id);
                            (removedChannel as IDisposable)?.Dispose();
                        }
                    }
                }
            }
        }

        public IEnumerable<IRpcWorkerChannel> GetChannels(string language)
        {
            if (language == null)
            {
                return GetChannels();
            }
            else if (_channels.TryGetValue(language, out IRpcWorkerChannelDictionary channels))
            {
                return channels.Values;
            }
            return Enumerable.Empty<IRpcWorkerChannel>();
        }

        public IEnumerable<IRpcWorkerChannel> GetChannels()
        {
            var rpcWorkerChannels = new List<IRpcWorkerChannel>();
            foreach (string language in _channels.Keys)
            {
                _channels.TryGetValue(language, out IRpcWorkerChannelDictionary channels);
                rpcWorkerChannels.AddRange(channels.Values);
            }
            return rpcWorkerChannels;
        }
    }
}

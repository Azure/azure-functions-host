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
        private ConcurrentDictionary<string, IRpcWorkerChannel> _channels = new ConcurrentDictionary<string, IRpcWorkerChannel>();
        private ConcurrentDictionary<string, HashSet<string>> _channelLanguage = new ConcurrentDictionary<string, HashSet<string>>();

        public JobHostRpcWorkerChannelManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<JobHostRpcWorkerChannelManager>();
        }

        public void AddChannel(IRpcWorkerChannel channel, string language)
        {
            _channels.TryAdd(channel.Id, channel);

            if (_channelLanguage.TryGetValue(language, out HashSet<string> list))
            {
                list.Add(channel.Id);
            }
            else
            {
                _channelLanguage.TryAdd(language, new HashSet<string>()
                {
                    {
                        channel.Id
                    }
                });
            }
        }

        public Task<bool> ShutdownChannelIfExistsAsync(string channelId, Exception workerException)
        {
            _channelLanguage.Clear();
            if (_channels.TryRemove(channelId, out IRpcWorkerChannel removedChannel))
            {
                _logger.LogDebug("Disposing JobHost language worker channel with id:{workerId}", removedChannel.Id);
                removedChannel.TryFailExecutions(workerException);
                (removedChannel as IDisposable)?.Dispose();
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public void ShutdownChannels()
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

        public IEnumerable<IRpcWorkerChannel> GetChannels(string language)
        {
            _channelLanguage.TryGetValue(language, out HashSet<string> list);
            return _channels.Where(channel => list.Contains(channel.Key)).Select(channel => channel.Value);
        }
    }
}

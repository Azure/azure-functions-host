// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class JobHostLanguageWorkerChannelManager : IJobHostLanguageWorkerChannelManager
    {
        private readonly ILogger _logger;
        private ConcurrentDictionary<string, ILanguageWorkerChannel> _channels = new ConcurrentDictionary<string, ILanguageWorkerChannel>();

        public JobHostLanguageWorkerChannelManager(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<JobHostLanguageWorkerChannelManager>();
        }

        public void AddChannel(ILanguageWorkerChannel channel)
        {
            _channels.TryAdd(channel.Id, channel);
        }

        public void DisposeAndRemoveChannel(ILanguageWorkerChannel channel)
        {
            if (_channels.TryRemove(channel.Id, out ILanguageWorkerChannel removedChannel))
            {
                _logger.LogDebug("Disposing language worker channel with id:{workerId}", removedChannel.Id);
                (removedChannel as IDisposable)?.Dispose();
            }
        }

        public void DisposeAndRemoveChannels()
        {
            foreach (string channelId in _channels.Keys)
            {
                if (_channels.TryRemove(channelId, out ILanguageWorkerChannel removedChannel))
                {
                    _logger.LogDebug("Disposing language worker channel with id:{workerId}", removedChannel.Id);
                    (removedChannel as IDisposable)?.Dispose();
                }
            }
        }

        public IEnumerable<ILanguageWorkerChannel> GetChannels()
        {
            return _channels.Values;
        }
    }
}

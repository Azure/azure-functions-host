// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class JobHostLanguageWorkerChannelManager : IJobHostLanguageWorkerChannelManager
    {
        private ConcurrentDictionary<string, ILanguageWorkerChannel> _channels = new ConcurrentDictionary<string, ILanguageWorkerChannel>();

        public void AddChannel(ILanguageWorkerChannel channel)
        {
            _channels.TryAdd(channel.Id, channel);
        }

        public void DisposeAndRemoveChannel(ILanguageWorkerChannel channel)
        {
            if (_channels.TryRemove(channel.Id, out ILanguageWorkerChannel removedChannel))
            {
                removedChannel?.Dispose();
            }
        }

        public void DisposeAndRemoveChannels()
        {
            foreach (string channelId in _channels.Keys)
            {
                if (_channels.TryRemove(channelId, out ILanguageWorkerChannel channel))
                {
                    channel?.Dispose();
                }
            }
        }

        public IEnumerable<ILanguageWorkerChannel> GetChannels()
        {
            return _channels.Values;
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerState
    {
        private object _lock = new object();

        private ConcurrentDictionary<string, ILanguageWorkerChannel> _channels = new ConcurrentDictionary<string, ILanguageWorkerChannel>();

        internal ConcurrentBag<Exception> Errors { get; set; } = new ConcurrentBag<Exception>();

        // Registered list of functions which can be replayed if the worker fails to start / errors
        internal ReplaySubject<FunctionMetadata> Functions { get; set; } = new ReplaySubject<FunctionMetadata>();

        internal void AddChannel(ILanguageWorkerChannel channel)
        {
            _channels.TryAdd(channel.Id, channel);
        }

        internal void DisposeAndRemoveChannel(ILanguageWorkerChannel channel)
        {
            if (_channels.TryRemove(channel.Id, out ILanguageWorkerChannel removedChannel))
            {
                removedChannel?.Dispose();
            }
        }

        internal void DisposeAndRemoveChannels()
        {
            foreach (string channelId in _channels.Keys)
            {
                if (_channels.TryRemove(channelId, out ILanguageWorkerChannel channel))
                {
                    channel?.Dispose();
                }
            }
        }

        internal IEnumerable<ILanguageWorkerChannel> GetChannels()
        {
            return _channels.Values;
        }
    }
}

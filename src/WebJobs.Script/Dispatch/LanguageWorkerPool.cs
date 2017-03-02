// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class LanguageWorkerPool : ILanguageWorkerPool
    {
        private IEnumerable<ILanguageWorkerChannel> _channels;
        private ConcurrentDictionary<FunctionMetadata, ILanguageWorkerChannel> _functionChannels = new ConcurrentDictionary<FunctionMetadata, ILanguageWorkerChannel>();

        public LanguageWorkerPool()
        {
            _channels = new List<LanguageWorkerChannel>()
            {
                new LanguageWorkerChannel()
            };
        }

        public Task Start()
        {
            var startTasks = _channels.Select(channel => channel.Start());
            return Task.WhenAll(startTasks);
        }

        public async Task Load(FunctionMetadata functionMetadata)
        {
            var channel = _channels.First();
            await channel.Load(functionMetadata);
            _functionChannels[functionMetadata] = channel;
        }

        public Task<object> Invoke(FunctionMetadata functionMetadata, object[] parameters)
        {
            var channel = _functionChannels[functionMetadata];
            return channel.Invoke(parameters);
        }
    }
}

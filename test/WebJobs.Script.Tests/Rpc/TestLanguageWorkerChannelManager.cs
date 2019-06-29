// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class TestLanguageWorkerChannelManager : IWebHostLanguageWorkerChannelManager
    {
        private IScriptEventManager _eventManager;
        private ILogger _testLogger;
        private ConcurrentDictionary<string, List<ILanguageWorkerChannel>> _workerChannels = new ConcurrentDictionary<string, List<ILanguageWorkerChannel>>();
        private string _scriptRootPath;

        public TestLanguageWorkerChannelManager(IScriptEventManager eventManager, ILogger testLogger, string scriptRootPath)
        {
            _eventManager = eventManager;
            _testLogger = testLogger;
            _scriptRootPath = scriptRootPath;
        }

        public ILanguageWorkerChannel GetChannel(string language)
        {
            throw new System.NotImplementedException();
        }

        public IEnumerable<ILanguageWorkerChannel> GetChannels(string language)
        {
            if (_workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public Task<ILanguageWorkerChannel> InitializeChannelAsync(string language)
        {
            ILanguageWorkerChannel workerChannel = new TestLanguageWorkerChannel(Guid.NewGuid().ToString(), language, _eventManager, _testLogger, true);
            if (_workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> workerChannels))
            {
                workerChannels.Add(workerChannel);
            }
            else
            {
                _workerChannels.TryAdd(language, new List<ILanguageWorkerChannel>());
                _workerChannels[language].Add(workerChannel);
            }
            workerChannel.StartWorkerProcessAsync();
            return Task.FromResult(workerChannel);
        }

        public void ShutdownChannels()
        {
        }

        public void ShutdownProcessRegistry()
        {
        }

        public bool ShutdownChannelIfExists(string language, string workerId)
        {
            if (_workerChannels.TryGetValue(language, out List<ILanguageWorkerChannel> workerChannels))
            {
                var channels = workerChannels.Where(ch => ch.Id == workerId);
                foreach (var channel in channels.ToList())
                {
                    workerChannels.Remove(channel);
                }
            }
            return false;
        }

        public void ShutdownStandbyChannels(IEnumerable<FunctionMetadata> functions)
        {
        }

        public Task SpecializeAsync()
        {
            throw new System.NotImplementedException();
        }
    }
}

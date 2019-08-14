﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        private ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>>();
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

        public Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> GetChannels(string language)
        {
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public Task<ILanguageWorkerChannel> InitializeChannelAsync(string language)
        {
            ILanguageWorkerChannel workerChannel = new TestLanguageWorkerChannel(Guid.NewGuid().ToString(), language, _eventManager, _testLogger, true);
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> workerChannels))
            {
                workerChannels.Add(workerChannel.Id, new TaskCompletionSource<ILanguageWorkerChannel>());
            }
            else
            {
                _workerChannels.TryAdd(language, new Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>>());
                _workerChannels[language].Add(workerChannel.Id, new TaskCompletionSource<ILanguageWorkerChannel>());
            }
            workerChannel.StartWorkerProcessAsync();
            SetInitializedWorkerChannel(language, workerChannel);
            return Task.FromResult(workerChannel);
        }

        public Task ShutdownChannelsAsync()
        {
            return Task.CompletedTask;
        }

        public void ShutdownProcessRegistry()
        {
        }

        public async Task<bool> ShutdownChannelIfExistsAsync(string language, string workerId)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> languageWorkerChannels))
            {
                if (languageWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<ILanguageWorkerChannel> value))
                {
                    ILanguageWorkerChannel channel = await value?.Task;
                    if (channel != null)
                    {
                        (channel as IDisposable)?.Dispose();
                        languageWorkerChannels.Remove(workerId);
                        return true;
                    }
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

        internal void SetInitializedWorkerChannel(string initializedRuntime, ILanguageWorkerChannel initializedLanguageWorkerChannel)
        {
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<ILanguageWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<ILanguageWorkerChannel> value))
                {
                    value.SetResult(initializedLanguageWorkerChannel);
                }
            }
        }
    }
}

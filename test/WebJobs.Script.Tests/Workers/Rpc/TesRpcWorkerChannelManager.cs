// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class TesRpcWorkerChannelManager : IWebHostRpcWorkerChannelManager
    {
        private IScriptEventManager _eventManager;
        private ILogger _testLogger;
        private ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>> _workerChannels = new ConcurrentDictionary<string, Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>>();
        private string _scriptRootPath;
        private IRpcWorkerChannelFactory _testLanguageWorkerChannelFactory;

        public TesRpcWorkerChannelManager(IScriptEventManager eventManager, ILogger testLogger, string scriptRootPath, IRpcWorkerChannelFactory testLanguageWorkerChannelFactory)
        {
            _eventManager = eventManager;
            _testLogger = testLogger;
            _scriptRootPath = scriptRootPath;
            _testLanguageWorkerChannelFactory = testLanguageWorkerChannelFactory;
        }

        public IRpcWorkerChannel GetChannel(string language)
        {
            throw new System.NotImplementedException();
        }

        public Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> GetChannels(string language)
        {
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> workerChannels))
            {
                return workerChannels;
            }
            return null;
        }

        public async Task<IRpcWorkerChannel> InitializeChannelAsync(string language)
        {
            var metricsLogger = new Mock<IMetricsLogger>();
            IRpcWorkerChannel workerChannel = _testLanguageWorkerChannelFactory.Create(_scriptRootPath, language, metricsLogger.Object, 0);
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> workerChannels))
            {
                workerChannels.Add(workerChannel.Id, new TaskCompletionSource<IRpcWorkerChannel>());
            }
            else
            {
                _workerChannels.TryAdd(language, new Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>>());
                _workerChannels[language].Add(workerChannel.Id, new TaskCompletionSource<IRpcWorkerChannel>());
            }

            await workerChannel.StartWorkerProcessAsync().ContinueWith(processStartTask =>
            {
                if (processStartTask.Status == TaskStatus.RanToCompletion)
                {
                    SetInitializedWorkerChannel(language, workerChannel);
                }
                else if (processStartTask.Status == TaskStatus.Faulted)
                {
                    SetExceptionOnInitializedWorkerChannel(language, workerChannel, processStartTask.Exception);
                }
            });
            return workerChannel;
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
            if (_workerChannels.TryGetValue(language, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> rpcWorkerChannels))
            {
                if (rpcWorkerChannels.TryGetValue(workerId, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    try
                    {
                        IRpcWorkerChannel channel = await value?.Task;
                        if (channel != null)
                        {
                            (channel as IDisposable)?.Dispose();
                            rpcWorkerChannels.Remove(workerId);
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        rpcWorkerChannels.Remove(workerId);
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

        internal void SetInitializedWorkerChannel(string initializedRuntime, IRpcWorkerChannel initializedLanguageWorkerChannel)
        {
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    value.SetResult(initializedLanguageWorkerChannel);
                }
            }
        }

        internal void SetExceptionOnInitializedWorkerChannel(string initializedRuntime, IRpcWorkerChannel initializedLanguageWorkerChannel, Exception exception)
        {
            if (_workerChannels.TryGetValue(initializedRuntime, out Dictionary<string, TaskCompletionSource<IRpcWorkerChannel>> channel))
            {
                if (channel.TryGetValue(initializedLanguageWorkerChannel.Id, out TaskCompletionSource<IRpcWorkerChannel> value))
                {
                    value.SetException(exception);
                }
            }
        }
    }
}

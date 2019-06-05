// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.Rpc
{
    public class TestLanguageWorkerChannel : ILanguageWorkerChannel
    {
        private string _workerId;
        private bool _isWebhostChannel;
        private IScriptEventManager _eventManager;
        private string _runtime;
        private ILogger _testLogger;
        private LanguageWorkerChannelState _state;

        public TestLanguageWorkerChannel(string workerId, string runtime = null, IScriptEventManager eventManager = null, ILogger testLogger = null, bool isWebhostChannel = false)
        {
            _workerId = workerId;
            _isWebhostChannel = isWebhostChannel;
            _eventManager = eventManager;
            _runtime = runtime;
            _testLogger = testLogger;
        }

        public string Id => _workerId;

        public IDictionary<string, BufferBlock<ScriptInvocationContext>> FunctionInputBuffers => throw new NotImplementedException();

        public LanguageWorkerChannelState State => _state;

        public void Dispose()
        {
        }

        public void SetupFunctionInvocationBuffers(IEnumerable<FunctionMetadata> functions)
        {
            _testLogger.LogInformation("SetupFunctionInvocationBuffers called");
        }

        public void SendFunctionLoadRequests()
        {
            _testLogger.LogInformation("RegisterFunctions called");
        }

        public void SendFunctionEnvironmentReloadRequest()
        {
            _testLogger.LogInformation("SendFunctionEnvironmentReloadRequest called");
        }

        public void SendInvocationRequest(ScriptInvocationContext context)
        {
            _testLogger.LogInformation("SendInvocationRequest called");
        }

        public Task StartWorkerProcessAsync()
        {
            // To verify FunctionDispatcher transistions
            Task.Delay(TimeSpan.FromMilliseconds(100));
            string workerVersion = Guid.NewGuid().ToString();
            IDictionary<string, string> workerCapabilities = new Dictionary<string, string>()
            {
                { "test", "testSupported" }
            };

            if (_isWebhostChannel)
            {
                RpcWebHostChannelReadyEvent readyEvent = new RpcWebHostChannelReadyEvent(_workerId, _runtime, this, workerVersion, workerCapabilities);
                _eventManager.Publish(readyEvent);
            }
            else
            {
                RpcJobHostChannelReadyEvent readyEvent = new RpcJobHostChannelReadyEvent(_workerId, _runtime, this, workerVersion, workerCapabilities);
                _eventManager.Publish(readyEvent);
            }
            _state = LanguageWorkerChannelState.Initialized;
            return Task.CompletedTask;
        }

        public void RaiseWorkerError()
        {
            Exception testEx = new Exception("Test Worker Error");
            _eventManager.Publish(new WorkerErrorEvent(_runtime, Id, testEx));
        }
    }
}

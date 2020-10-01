// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.Rpc
{
    public class TestRpcWorkerChannelFactory : IRpcWorkerChannelFactory
    {
        private IScriptEventManager _eventManager;
        private ILogger _testLogger;
        private ConcurrentDictionary<string, List<IRpcWorkerChannel>> _workerChannels = new ConcurrentDictionary<string, List<IRpcWorkerChannel>>();
        private string _scriptRootPath;
        private bool _throwOnProcessStartUp;

        public TestRpcWorkerChannelFactory(IScriptEventManager eventManager, ILogger testLogger, string scriptRootPath, bool throwOnProcessStartUp = false)
        {
            _eventManager = eventManager;
            _testLogger = testLogger;
            _scriptRootPath = scriptRootPath;
            _throwOnProcessStartUp = throwOnProcessStartUp;
    }

        public IRpcWorkerChannel Create(string scriptRootPath, string language, IMetricsLogger metricsLogger, int attemptCount, IEnumerable<RpcWorkerConfig> workerConfigs)
        {
            return new TestRpcWorkerChannel(Guid.NewGuid().ToString(), language, _eventManager, _testLogger, throwOnProcessStartUp: _throwOnProcessStartUp);
        }
    }
}

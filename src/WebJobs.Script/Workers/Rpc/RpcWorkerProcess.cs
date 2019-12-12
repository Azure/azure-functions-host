// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    internal class RpcWorkerProcess : WorkerProcess
    {
        private readonly IWorkerProcessFactory _processFactory;
        private readonly ILogger _workerProcessLogger;
        private readonly IScriptEventManager _eventManager;
        private readonly string _runtime;
        private readonly string _workerId;
        private readonly Uri _serverUri;
        private readonly string _scriptRootPath;
        private readonly WorkerProcessArguments _workerProcessArguments;

        internal RpcWorkerProcess(string runtime,
                                       string workerId,
                                       string rootScriptPath,
                                       Uri serverUri,
                                       WorkerProcessArguments workerProcessArguments,
                                       IScriptEventManager eventManager,
                                       IWorkerProcessFactory processFactory,
                                       IProcessRegistry processRegistry,
                                       ILogger workerProcessLogger,
                                       IWorkerConsoleLogSource consoleLogSource)
            : base(eventManager, processRegistry, workerProcessLogger, consoleLogSource)
        {
            _runtime = runtime;
            _processFactory = processFactory;
            _eventManager = eventManager;
            _workerProcessLogger = workerProcessLogger;
            _workerId = workerId;
            _serverUri = serverUri;
            _scriptRootPath = rootScriptPath;
            _workerProcessArguments = workerProcessArguments;
        }

        internal override Process CreateWorkerProcess()
        {
            var workerContext = new RpcWorkerContext(Guid.NewGuid().ToString(), RpcWorkerConstants.DefaultMaxMessageLengthBytes, _workerId, _workerProcessArguments, _scriptRootPath, _serverUri);
            return _processFactory.CreateWorkerProcess(workerContext);
        }

        internal override void HandleWorkerProcessExitError(WorkerProcessExitException langExc)
        {
            if (langExc == null)
            {
                throw new ArgumentNullException("WorkerProcessExitException for rpc worker process exit must exist.");
            }
            // The subscriber of WorkerErrorEvent is expected to Dispose() the errored channel
            _workerProcessLogger.LogDebug(langExc, $"Language Worker Process exited. Pid={langExc.Pid}.", _workerProcessArguments.ExecutablePath);
            _eventManager.Publish(new WorkerErrorEvent(_runtime, _workerId, langExc));
        }

        internal override void HandleWorkerProcessRestart()
        {
            _workerProcessLogger?.LogInformation("Language Worker Process exited and needs to be restarted.");
            _eventManager.Publish(new WorkerRestartEvent(_runtime, _workerId));
        }
    }
}
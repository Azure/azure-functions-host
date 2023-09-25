// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        private readonly string _workerDirectory;
        private readonly IOptions<FunctionsHostingConfigOptions> _hostingConfigOptions;
        private readonly IEnvironment _environment;

        internal RpcWorkerProcess(string runtime,
                                       string workerId,
                                       string rootScriptPath,
                                       Uri serverUri,
                                       RpcWorkerConfig rpcWorkerConfig,
                                       IScriptEventManager eventManager,
                                       IWorkerProcessFactory processFactory,
                                       IProcessRegistry processRegistry,
                                       ILogger workerProcessLogger,
                                       IWorkerConsoleLogSource consoleLogSource,
                                       IMetricsLogger metricsLogger,
                                       IServiceProvider serviceProvider,
                                       IOptions<FunctionsHostingConfigOptions> hostingConfigOptions,
                                       IEnvironment environment,
                                       ILoggerFactory loggerFactory)
            : base(eventManager, processRegistry, workerProcessLogger, consoleLogSource, metricsLogger, serviceProvider, loggerFactory, environment, rpcWorkerConfig.Description.UseStdErrorStreamForErrorsOnly)
        {
            _runtime = runtime;
            _processFactory = processFactory;
            _eventManager = eventManager;
            _workerProcessLogger = workerProcessLogger;
            _workerId = workerId;
            _serverUri = serverUri;
            _scriptRootPath = rootScriptPath;
            _workerProcessArguments = rpcWorkerConfig.Arguments;
            _workerDirectory = rpcWorkerConfig.Description.WorkerDirectory;
            _hostingConfigOptions = hostingConfigOptions;
            _environment = environment;
        }

        internal override Process CreateWorkerProcess()
        {
            var workerContext = new RpcWorkerContext(Guid.NewGuid().ToString(), RpcWorkerConstants.DefaultMaxMessageLengthBytes, _workerId, _workerProcessArguments, _scriptRootPath, _serverUri);
            workerContext.EnvironmentVariables.Add(WorkerConstants.FunctionsWorkerDirectorySettingName, _workerDirectory);
            workerContext.EnvironmentVariables.Add(WorkerConstants.FunctionsApplicationDirectorySettingName, _scriptRootPath);
            workerContext.EnvironmentVariables.Add(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName, _environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeVersionSettingName));
            foreach (var pair in _hostingConfigOptions.Value.Features)
            {
                workerContext.EnvironmentVariables[pair.Key] = pair.Value;
            }
            return _processFactory.CreateWorkerProcess(workerContext);
        }

        internal override void HandleWorkerProcessExitError(WorkerProcessExitException rpcWorkerProcessExitException)
        {
            if (Disposing)
            {
                return;
            }
            if (rpcWorkerProcessExitException == null)
            {
                throw new ArgumentNullException(nameof(rpcWorkerProcessExitException));
            }
            // The subscriber of WorkerErrorEvent is expected to Dispose() the errored channel
            _workerProcessLogger.LogError(rpcWorkerProcessExitException, $"Language Worker Process exited. Pid={rpcWorkerProcessExitException.Pid}.", _workerProcessArguments.ExecutablePath);
            _eventManager.Publish(new WorkerErrorEvent(_runtime, _workerId, rpcWorkerProcessExitException));
        }

        internal override void HandleWorkerProcessRestart()
        {
            _workerProcessLogger?.LogInformation("Language Worker Process exited and needs to be restarted.");
            _eventManager.Publish(new WorkerRestartEvent(_runtime, _workerId));
        }
    }
}
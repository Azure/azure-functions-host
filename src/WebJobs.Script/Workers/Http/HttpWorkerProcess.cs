// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    internal class HttpWorkerProcess : WorkerProcess
    {
        private readonly IWorkerProcessFactory _processFactory;
        private readonly ILogger _workerProcessLogger;
        private readonly IScriptEventManager _eventManager;
        private readonly HttpWorkerOptions _httpWorkerOptions;
        private readonly string _scriptRootPath;
        private readonly string _workerId;
        private readonly WorkerProcessArguments _workerProcessArguments;

        internal HttpWorkerProcess(string workerId,
                                       string rootScriptPath,
                                       HttpWorkerOptions httpWorkerOptions,
                                       IScriptEventManager eventManager,
                                       IWorkerProcessFactory processFactory,
                                       IProcessRegistry processRegistry,
                                       ILogger workerProcessLogger,
                                       IWorkerConsoleLogSource consoleLogSource,
                                       IEnvironment environment,
                                       IMetricsLogger metricsLogger,
                                       IServiceProvider serviceProvider,
                                       ILoggerFactory loggerFactory)
            : base(eventManager, processRegistry, workerProcessLogger, consoleLogSource, metricsLogger, serviceProvider, loggerFactory, environment, httpWorkerOptions.Description.UseStdErrorStreamForErrorsOnly)
        {
            _processFactory = processFactory;
            _eventManager = eventManager;
            _workerProcessLogger = workerProcessLogger;
            _workerId = workerId;
            _scriptRootPath = rootScriptPath;
            _httpWorkerOptions = httpWorkerOptions;
            _workerProcessArguments = _httpWorkerOptions.Arguments;
        }

        internal override Process CreateWorkerProcess()
        {
            var workerContext = new HttpWorkerContext()
            {
                RequestId = Guid.NewGuid().ToString(),
                WorkerId = _workerId,
                Arguments = _workerProcessArguments,
                WorkingDirectory = _httpWorkerOptions.Description.WorkingDirectory,
                Port = _httpWorkerOptions.Port
            };
            workerContext.EnvironmentVariables.Add(HttpWorkerConstants.PortEnvVarName, _httpWorkerOptions.Port.ToString());
            workerContext.EnvironmentVariables.Add(HttpWorkerConstants.WorkerIdEnvVarName, _workerId);
            workerContext.EnvironmentVariables.Add(HttpWorkerConstants.CustomHandlerPortEnvVarName, _httpWorkerOptions.Port.ToString());
            workerContext.EnvironmentVariables.Add(HttpWorkerConstants.CustomHandlerWorkerIdEnvVarName, _workerId);
            workerContext.EnvironmentVariables.Add(HttpWorkerConstants.FunctionAppRootVarName, _scriptRootPath);

            return _processFactory.CreateWorkerProcess(workerContext);
        }

        internal override void HandleWorkerProcessExitError(WorkerProcessExitException httpWorkerProcessExitException)
        {
            if (httpWorkerProcessExitException == null)
            {
                throw new ArgumentNullException(nameof(httpWorkerProcessExitException));
            }
            // The subscriber of WorkerErrorEvent is expected to Dispose() the errored channel
            _workerProcessLogger.LogError(httpWorkerProcessExitException, $"Language Worker Process exited. Pid={httpWorkerProcessExitException.Pid}.", _workerProcessArguments.ExecutablePath);
            _eventManager.Publish(new HttpWorkerErrorEvent(_workerId, httpWorkerProcessExitException));
        }

        internal override void HandleWorkerProcessRestart()
        {
            _workerProcessLogger?.LogInformation("Language Worker Process exited and needs to be restarted.");
            _eventManager.Publish(new HttpWorkerRestartEvent(_workerId));
        }
    }
}
﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal abstract class WorkerProcess : IWorkerProcess, IDisposable
    {
        private readonly IProcessRegistry _processRegistry;
        private readonly ILogger _workerProcessLogger;
        private readonly IWorkerConsoleLogSource _consoleLogSource;
        private readonly IScriptEventManager _eventManager;
        private readonly IMetricsLogger _metricsLogger;

        private Process _process;
        private bool _useStdErrorStreamForErrorsOnly;
        private ProcessMonitor _processMonitor;
        private bool _disposing;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);

        internal WorkerProcess(IScriptEventManager eventManager, IProcessRegistry processRegistry, ILogger workerProcessLogger, IWorkerConsoleLogSource consoleLogSource, IMetricsLogger metricsLogger, bool useStdErrStreamForErrorsOnly = false)
        {
            _processRegistry = processRegistry;
            _workerProcessLogger = workerProcessLogger;
            _consoleLogSource = consoleLogSource;
            _eventManager = eventManager;
            _metricsLogger = metricsLogger;
            _useStdErrorStreamForErrorsOnly = useStdErrStreamForErrorsOnly;
        }

        public int Id => _process.Id;

        internal Queue<string> ProcessStdErrDataQueue => _processStdErrDataQueue;

        internal abstract Process CreateWorkerProcess();

        public Task StartProcessAsync()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.ProcessStart))
            {
                _process = CreateWorkerProcess();
                try
                {
                    _process.ErrorDataReceived += (sender, e) => OnErrorDataReceived(sender, e);
                    _process.OutputDataReceived += (sender, e) => OnOutputDataReceived(sender, e);
                    _process.Exited += (sender, e) => OnProcessExited(sender, e);
                    _process.EnableRaisingEvents = true;

                    _workerProcessLogger?.LogDebug($"Starting worker process with FileName:{_process.StartInfo.FileName} WorkingDirectory:{_process.StartInfo.WorkingDirectory} Arguments:{_process.StartInfo.Arguments}");
                    _process.Start();
                    _workerProcessLogger?.LogDebug($"{_process.StartInfo.FileName} process with Id={_process.Id} started");

                    _process.BeginErrorReadLine();
                    _process.BeginOutputReadLine();

                    // Register process only after it starts
                    _processRegistry?.Register(_process);

                    _processMonitor = new ProcessMonitor(_process, SystemEnvironment.Instance);
                    _processMonitor.Start();

                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _workerProcessLogger.LogError(ex, $"Failed to start Worker Channel. Process fileName: {_process.StartInfo.FileName}");
                    return Task.FromException(ex);
                }
            }
        }

        public ProcessStats GetStats()
        {
            return _processMonitor.GetStats();
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                ParseErrorMessageAndLog(e.Data);
            }
        }

        internal void ParseErrorMessageAndLog(string msg)
        {
            if (msg.IndexOf("warn", StringComparison.OrdinalIgnoreCase) > -1)
            {
                BuildAndLogConsoleLog(msg, LogLevel.Warning);
            }
            else
            {
                if (_useStdErrorStreamForErrorsOnly)
                {
                    LogError(msg);
                }
                else
                {
                    if ((msg.IndexOf("error", StringComparison.OrdinalIgnoreCase) > -1) ||
                              (msg.IndexOf("fail", StringComparison.OrdinalIgnoreCase) > -1) ||
                              (msg.IndexOf("severe", StringComparison.OrdinalIgnoreCase) > -1))
                    {
                        LogError(msg);
                    }
                    else
                    {
                        BuildAndLogConsoleLog(msg, LogLevel.Information);
                    }
                }
            }
        }

        private void LogError(string msg)
        {
            BuildAndLogConsoleLog(msg, LogLevel.Error);
            _processStdErrDataQueue = WorkerProcessUtilities.AddStdErrMessage(_processStdErrDataQueue, Sanitizer.Sanitize(msg));
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            if (_disposing)
            {
                // No action needed
                return;
            }
            string exceptionMessage = string.Join(",", _processStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
            try
            {
                if (_process.ExitCode == WorkerConstants.SuccessExitCode)
                {
                    _process.WaitForExit();
                    _process.Close();
                }
                else if (_process.ExitCode == WorkerConstants.IntentionalRestartExitCode)
                {
                    HandleWorkerProcessRestart();
                }
                else
                {
                    var processExitEx = new WorkerProcessExitException($"{_process.StartInfo.FileName} exited with code {_process.ExitCode}\n {exceptionMessage}");
                    processExitEx.ExitCode = _process.ExitCode;
                    processExitEx.Pid = _process.Id;
                    HandleWorkerProcessExitError(processExitEx);
                }
            }
            catch (Exception exc)
            {
                _workerProcessLogger?.LogDebug(exc, "Exception on worker process exit.");
                // ignore process is already disposed
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                BuildAndLogConsoleLog(e.Data, LogLevel.Information);
            }
        }

        private void BuildAndLogConsoleLog(string msg, LogLevel level)
        {
            ConsoleLog consoleLog = new ConsoleLog()
            {
                Message = msg,
                Level = level
            };
            if (WorkerProcessUtilities.IsConsoleLog(msg))
            {
                consoleLog.Message = WorkerProcessUtilities.RemoveLogPrefix(msg);
                _workerProcessLogger?.Log(level, msg);
            }
            else
            {
                _consoleLogSource?.Log(consoleLog);
            }
        }

        internal abstract void HandleWorkerProcessExitError(WorkerProcessExitException langExc);

        internal abstract void HandleWorkerProcessRestart();

        public void Dispose()
        {
            _disposing = true;
            // best effort process disposal
            try
            {
                if (_process != null)
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill();
                        _process.WaitForExit();
                    }
                    _process.Dispose();
                }
                _processMonitor.Dispose();
            }
            catch (Exception exc)
            {
                _workerProcessLogger?.LogDebug(exc, "Exception on worker disposal.");
                //ignore
            }
        }
    }
}
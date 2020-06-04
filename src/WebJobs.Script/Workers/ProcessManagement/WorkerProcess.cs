// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
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

        private Process _process;
        private ProcessMonitor _processMonitor;
        private bool _disposing;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);

        internal WorkerProcess(IScriptEventManager eventManager, IProcessRegistry processRegistry, ILogger workerProcessLogger, IWorkerConsoleLogSource consoleLogSource)
        {
            _processRegistry = processRegistry;
            _workerProcessLogger = workerProcessLogger;
            _consoleLogSource = consoleLogSource;
            _eventManager = eventManager;
        }

        public int Id => _process.Id;

        internal Queue<string> ProcessStdErrDataQueue => _processStdErrDataQueue;

        internal abstract Process CreateWorkerProcess();

        public Task StartProcessAsync()
        {
            _process = CreateWorkerProcess();
            try
            {
                _process.ErrorDataReceived += (sender, e) => OnErrorDataReceived(sender, e);
                _process.OutputDataReceived += (sender, e) => OnOutputDataReceived(sender, e);
                _process.Exited += (sender, e) => OnProcessExited(sender, e);
                _process.EnableRaisingEvents = true;

                _workerProcessLogger?.LogInformation($"Starting worker process:{_process.StartInfo.FileName} {_process.StartInfo.Arguments}");
                _process.Start();
                _workerProcessLogger?.LogInformation($"{_process.StartInfo.FileName} process with Id={_process.Id} started");

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
                _workerProcessLogger.LogError(ex, "Failed to start Worker Channel");
                return Task.FromException(ex);
            }
        }

        public ProcessStats GetStats()
        {
            return _processMonitor.GetStats();
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            // TODO: per language stdout/err parser?
            if (e.Data != null)
            {
                string msg = e.Data;
                if (msg.IndexOf("warn", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    if (WorkerProcessUtilities.IsConsoleLog(msg))
                    {
                        msg = WorkerProcessUtilities.RemoveLogPrefix(msg);
                        _workerProcessLogger?.LogWarning(msg);
                    }
                    else
                    {
                        _consoleLogSource?.Log(msg);
                    }
                }
                else if ((msg.IndexOf("error", StringComparison.OrdinalIgnoreCase) > -1) ||
                          (msg.IndexOf("fail", StringComparison.OrdinalIgnoreCase) > -1) ||
                          (msg.IndexOf("severe", StringComparison.OrdinalIgnoreCase) > -1))
                {
                    if (WorkerProcessUtilities.IsConsoleLog(msg))
                    {
                        msg = WorkerProcessUtilities.RemoveLogPrefix(msg);
                        _workerProcessLogger?.LogError(msg);
                    }
                    else
                    {
                        _consoleLogSource?.Log(msg);
                    }
                    _processStdErrDataQueue = WorkerProcessUtilities.AddStdErrMessage(_processStdErrDataQueue, Sanitizer.Sanitize(msg));
                }
                else
                {
                    if (WorkerProcessUtilities.IsConsoleLog(msg))
                    {
                        msg = WorkerProcessUtilities.RemoveLogPrefix(msg);
                        _workerProcessLogger?.LogInformation(msg);
                    }
                    else
                    {
                        _consoleLogSource?.Log(msg);
                    }
                }
            }
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
                string msg = e.Data;
                if (WorkerProcessUtilities.IsConsoleLog(msg))
                {
                    msg = WorkerProcessUtilities.RemoveLogPrefix(msg);
                    _workerProcessLogger?.LogInformation(msg);
                }
                else
                {
                    _consoleLogSource?.Log(msg);
                }
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
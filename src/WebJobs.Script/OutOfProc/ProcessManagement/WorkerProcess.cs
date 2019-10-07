// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.OutOfProc;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal abstract class WorkerProcess : ILanguageWorkerProcess, IDisposable
    {
        private readonly IProcessRegistry _processRegistry;
        private readonly ILogger _workerProcessLogger;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;
        private readonly IScriptEventManager _eventManager;

        private Process _process;
        private bool _disposing;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);

        internal WorkerProcess(IScriptEventManager eventManager, IProcessRegistry processRegistry, ILogger workerProcessLogger, ILanguageWorkerConsoleLogSource consoleLogSource)
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

                _workerProcessLogger?.LogInformation($"Starting language worker process:{_process.StartInfo.FileName} {_process.StartInfo.Arguments}");
                _process.Start();
                _workerProcessLogger?.LogInformation($"{_process.StartInfo.FileName} process with Id={_process.Id} started");

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();

                // Register process only after it starts
                _processRegistry?.Register(_process);
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _workerProcessLogger.LogError(ex, "Failed to start Worker Channel");
                return Task.FromException(ex);
            }
        }

        private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            // TODO: per language stdout/err parser?
            if (e.Data != null)
            {
                string msg = e.Data;
                if (msg.IndexOf("warn", StringComparison.OrdinalIgnoreCase) > -1)
                {
                    if (LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = LanguageWorkerChannelUtilities.RemoveLogPrefix(msg);
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
                    if (LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = LanguageWorkerChannelUtilities.RemoveLogPrefix(msg);
                        _workerProcessLogger?.LogError(msg);
                    }
                    else
                    {
                        _consoleLogSource?.Log(msg);
                    }
                    _processStdErrDataQueue = LanguageWorkerChannelUtilities.AddStdErrMessage(_processStdErrDataQueue, Sanitizer.Sanitize(msg));
                }
                else
                {
                    if (LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg))
                    {
                        msg = LanguageWorkerChannelUtilities.RemoveLogPrefix(msg);
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
                if (_process.ExitCode == OutOfProcConstants.SuccessExitCode)
                {
                    _process.WaitForExit();
                    _process.Close();
                }
                else if (_process.ExitCode == OutOfProcConstants.IntentionalRestartExitCode)
                {
                    HandleWorkerProcessRestart();
                }
                else
                {
                    var processExitEx = new LanguageWorkerProcessExitException($"{_process.StartInfo.FileName} exited with code {_process.ExitCode}\n {exceptionMessage}");
                    processExitEx.ExitCode = _process.ExitCode;
                    HandleWorkerProcessExitError(processExitEx);
                }
            }
            catch (Exception)
            {
                // ignore process is already disposed
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                string msg = e.Data;
                if (LanguageWorkerChannelUtilities.IsLanguageWorkerConsoleLog(msg))
                {
                    msg = LanguageWorkerChannelUtilities.RemoveLogPrefix(msg);
                    _workerProcessLogger?.LogInformation(msg);
                }
                else
                {
                    _consoleLogSource?.Log(msg);
                }
            }
        }

        internal abstract void HandleWorkerProcessExitError(LanguageWorkerProcessExitException langExc);

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
            }
            catch (Exception)
            {
                //ignore
            }
        }
    }
}
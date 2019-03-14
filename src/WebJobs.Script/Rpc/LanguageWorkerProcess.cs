// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerProcess : ILanguageWorkerProcess
    {
        private readonly IWorkerProcessFactory _processFactory;
        private readonly IProcessRegistry _processRegistry;
        private readonly ILogger _workerChannelLogger;
        private readonly ILanguageWorkerConsoleLogSource _consoleLogSource;
        private readonly IScriptEventManager _eventManager;

        private Process _process;
        private string _runtime;
        private string _workerId;
        private bool _disposing;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);

        internal LanguageWorkerProcess()
        {
            // To help with unit tests
        }

        internal LanguageWorkerProcess(string runtime,
                                       string workerId,
                                       WorkerContext workerContext,
                                       IScriptEventManager eventManager,
                                       IWorkerProcessFactory processFactory,
                                       IProcessRegistry processRegistry,
                                       ILoggerFactory loggerFactory,
                                       ILanguageWorkerConsoleLogSource consoleLogSource)
        {
            _runtime = runtime;
            _workerId = workerId;
            _processFactory = processFactory;
            _processRegistry = processRegistry;
            _workerChannelLogger = loggerFactory.CreateLogger($"LanguageWorkerProcess.{runtime}.{workerId}");
            _consoleLogSource = consoleLogSource;
            _eventManager = eventManager;
            _process = _processFactory.CreateWorkerProcess(workerContext);
        }

        public Process WorkerProcess => _process;

        internal Queue<string> ProcessStdErrDataQueue => _processStdErrDataQueue;

        public Process StartProcess()
        {
            try
            {
                _process.ErrorDataReceived += (sender, e) => OnErrorDataReceived(sender, e);
                _process.OutputDataReceived += (sender, e) => OnOutputDataReceived(sender, e);
                _process.Exited += (sender, e) => OnProcessExited(sender, e);
                _process.EnableRaisingEvents = true;

                _workerChannelLogger?.LogInformation($"Starting language worker process:{_process.StartInfo.FileName} {_process.StartInfo.Arguments}");
                _process.Start();
                _workerChannelLogger?.LogInformation($"{_process.StartInfo.FileName} process with Id={_process.Id} started");

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();

                // Register process only after it starts
                _processRegistry?.Register(_process);
            }
            catch (Exception ex)
            {
                throw new HostInitializationException($"Failed to start Language Worker Channel for language :{_runtime}", ex);
            }

            return _process;
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
                        _workerChannelLogger?.LogWarning(msg);
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
                        _workerChannelLogger?.LogError(msg);
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
                        _workerChannelLogger?.LogInformation(msg);
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
                if (_process.ExitCode != 0)
                {
                    var processExitEx = new LanguageWorkerProcessExitException($"{_process.StartInfo.FileName} exited with code {_process.ExitCode}\n {exceptionMessage}");
                    processExitEx.ExitCode = _process.ExitCode;
                    HandleWorkerPorcessExitError(processExitEx);
                }
                else
                {
                    _process.WaitForExit();
                    _process.Close();
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
                    _workerChannelLogger?.LogInformation(msg);
                }
                else
                {
                    _consoleLogSource?.Log(msg);
                }
            }
        }

        internal void HandleWorkerPorcessExitError(LanguageWorkerProcessExitException langExc)
        {
            // The subscriber of WorkerErrorEvent is expected to Dispose() the errored channel
            if (langExc != null && langExc.ExitCode != -1)
            {
                _workerChannelLogger.LogDebug(langExc, $"Language Worker Process exited.", _process.StartInfo.FileName);
                _eventManager.Publish(new WorkerErrorEvent(_runtime, _workerId, langExc));
            }
        }

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
            catch (Exception e)
            {
                _workerChannelLogger.LogDebug(e, "LanguageWorkerChannel Dispose failure");
            }
        }
    }
}

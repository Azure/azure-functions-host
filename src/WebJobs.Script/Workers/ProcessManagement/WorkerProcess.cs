// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mono.Unix;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal abstract class WorkerProcess : IWorkerProcess, IDisposable
    {
        private readonly IProcessRegistry _processRegistry;
        private readonly ILogger _workerProcessLogger;
        private readonly IWorkerConsoleLogSource _consoleLogSource;
        private readonly IScriptEventManager _eventManager;
        private readonly IMetricsLogger _metricsLogger;
        private readonly int processExitTimeoutInMilliseconds = 1000;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDisposable _eventSubscription;
        private readonly Lazy<ILogger> _toolingConsoleJsonLoggerLazy;
        private readonly IEnvironment _environment;

        private bool _useStdErrorStreamForErrorsOnly;
        private Queue<string> _processStdErrDataQueue = new Queue<string>(3);
        private IHostProcessMonitor _processMonitor;
        private object _syncLock = new object();

        internal WorkerProcess(IScriptEventManager eventManager, IProcessRegistry processRegistry, ILogger workerProcessLogger, IWorkerConsoleLogSource consoleLogSource, IMetricsLogger metricsLogger, IServiceProvider serviceProvider, ILoggerFactory loggerFactory, IEnvironment environment, bool useStdErrStreamForErrorsOnly = false)
        {
            _processRegistry = processRegistry;
            _workerProcessLogger = workerProcessLogger;
            _consoleLogSource = consoleLogSource;
            _eventManager = eventManager;
            _metricsLogger = metricsLogger;
            _useStdErrorStreamForErrorsOnly = useStdErrStreamForErrorsOnly;
            _serviceProvider = serviceProvider;
            _environment = environment;
            _toolingConsoleJsonLoggerLazy = new Lazy<ILogger>(() => loggerFactory.CreateLogger(WorkerConstants.ToolingConsoleLogCategoryName), isThreadSafe: true);

            // We subscribe to host start events so we can handle the restart that occurs
            // on host specialization.
            _eventSubscription = _eventManager.OfType<HostStartEvent>().Subscribe(OnHostStart);
        }

        protected bool Disposing { get; private set; }

        public int Id => Process.Id;

        internal Queue<string> ProcessStdErrDataQueue => _processStdErrDataQueue;

        public Process Process { get; set; }

        public TaskCompletionSource<bool> ProcessWaitingForTermination { get; set; } = new TaskCompletionSource<bool>();

        internal abstract Process CreateWorkerProcess();

        public Task StartProcessAsync()
        {
            using (_metricsLogger.LatencyEvent(MetricEventNames.ProcessStart))
            {
                Process = CreateWorkerProcess();
                if (_environment.IsAnyLinuxConsumption())
                {
                    AssignUserExecutePermissionsIfNotExists();
                }

                try
                {
                    Process.ErrorDataReceived += (sender, e) => OnErrorDataReceived(sender, e);
                    Process.OutputDataReceived += (sender, e) => OnOutputDataReceived(sender, e);
                    Process.Exited += (sender, e) => OnProcessExited(sender, e);
                    Process.EnableRaisingEvents = true;

                    _workerProcessLogger?.LogDebug($"Starting worker process with FileName:{Process.StartInfo.FileName} WorkingDirectory:{Process.StartInfo.WorkingDirectory} Arguments:{Process.StartInfo.Arguments}");
                    Process.Start();
                    _workerProcessLogger?.LogDebug($"{Process.StartInfo.FileName} process with Id={Process.Id} started");

                    Process.BeginErrorReadLine();
                    Process.BeginOutputReadLine();

                    // Register process only after it starts
                    _processRegistry?.Register(this);

                    RegisterWithProcessMonitor();

                    return Task.CompletedTask;
                }
                catch (Exception ex)
                {
                    _workerProcessLogger.LogError(ex, $"Failed to start Worker Channel. Process fileName: {Process.StartInfo.FileName}");
                    return Task.FromException(ex);
                }
            }
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
                    // TODO: redesign how we log errors so it's not based on the string contents (GH issue #8273)
                    if ((msg.IndexOf("error", StringComparison.OrdinalIgnoreCase) > -1) ||
                        (msg.IndexOf("fail", StringComparison.OrdinalIgnoreCase) > -1) ||
                        (msg.IndexOf("severe", StringComparison.OrdinalIgnoreCase) > -1) ||
                        (msg.IndexOf("unhandled exception", StringComparison.OrdinalIgnoreCase) > -1))
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
            _workerProcessLogger.LogDebug("Process {processId} has exited with code {exitCode}.", Process?.Id, Process?.ExitCode);

            if (Disposing)
            {
                // No action needed
                return;
            }

            try
            {
                if (Process.ExitCode == WorkerConstants.SuccessExitCode)
                {
                    Process.WaitForExit();
                    Process.Close();
                }
                else if (Process.ExitCode == WorkerConstants.IntentionalRestartExitCode)
                {
                    HandleWorkerProcessRestart();
                }
                else
                {
                    string exceptionMessage = string.Join(",", _processStdErrDataQueue.Where(s => !string.IsNullOrEmpty(s)));
                    var processExitEx = new WorkerProcessExitException($"{Process.StartInfo.FileName} exited with code {Process.ExitCode} (0x{Process.ExitCode.ToString("X")})", new Exception(exceptionMessage));
                    processExitEx.ExitCode = Process.ExitCode;
                    processExitEx.Pid = Process.Id;
                    HandleWorkerProcessExitError(processExitEx);
                }
            }
            catch (Exception exc)
            {
                _workerProcessLogger?.LogDebug(exc, "Exception on worker process exit. Process id: {processId}", Process?.Id);
                // ignore process is already disposed
            }
            finally
            {
                UnregisterFromProcessMonitor();
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                BuildAndLogConsoleLog(e.Data, LogLevel.Information);
            }
        }

        internal void BuildAndLogConsoleLog(string msg, LogLevel level)
        {
            ConsoleLog consoleLog = new ConsoleLog()
            {
                Message = msg,
                Level = level
            };

            if (WorkerProcessUtilities.IsConsoleLog(msg))
            {
                _workerProcessLogger?.Log(level, WorkerProcessUtilities.RemoveLogPrefix(msg));
            }
            else if (WorkerProcessUtilities.IsToolingConsoleJsonLogEntry(msg))
            {
                // log with the message prefix as coretools expects it.
                _toolingConsoleJsonLoggerLazy.Value.Log(level, msg);
            }
            else
            {
                _consoleLogSource?.Log(consoleLog);
            }
        }

        internal abstract void HandleWorkerProcessExitError(WorkerProcessExitException langExc);

        internal abstract void HandleWorkerProcessRestart();

        public void WaitForProcessExitInMilliSeconds(int waitTime)
        {
            try
            {
                if (!Process.HasExited)
                {
                    Process.WaitForExit(waitTime);
                }
            }
            catch (Exception ex)
            {
                _workerProcessLogger.LogDebug(ex, "An exception was thrown while waiting for a worker process to exit. It is possible that the process had already exited and this can be ignored.");
            }
        }

        public void Dispose()
        {
            Disposing = true;
            // best effort process disposal

            ProcessWaitingForTermination.SetResult(false);

            try
            {
                _eventSubscription?.Dispose();

                if (Process != null)
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill();
                        if (!Process.WaitForExit(processExitTimeoutInMilliseconds))
                        {
                            _workerProcessLogger.LogWarning("Worker process {processId} has not exited despite waiting for {processExitTimeoutInMilliseconds} ms", Process?.Id, processExitTimeoutInMilliseconds);
                        }
                    }
                    Process.Dispose();
                }
            }
            catch (Exception exc)
            {
                _workerProcessLogger?.LogDebug(exc, "Exception on worker disposal.");
                //ignore
            }
        }

        internal void OnHostStart(HostStartEvent evt)
        {
            if (!Disposing)
            {
                RegisterWithProcessMonitor();
            }
        }

        /// <summary>
        /// Ensures that our process is registered with <see cref="IHostProcessMonitor"/>.
        /// </summary>
        /// <remarks>
        /// The goal is to ensure that all worker processes are registered with the monitor for the active host.
        /// There are a few different cases to consider:
        /// - Starting up in normal mode, we register on when the process is started.
        /// - When a placeholder mode host is specialized, a new host will be started but the previously initialized
        ///   worker process will remain running. We need to re-register with the new host.
        /// - When the worker process dies and a new instance of this class is created.
        /// </remarks>
        internal void RegisterWithProcessMonitor()
        {
            var processMonitor = _serviceProvider.GetScriptHostServiceOrNull<IHostProcessMonitor>();
            lock (_syncLock)
            {
                if (processMonitor != null && processMonitor != _processMonitor && Process != null)
                {
                    processMonitor.RegisterChildProcess(Process);
                    _processMonitor = processMonitor;
                }
            }
        }

        internal void UnregisterFromProcessMonitor()
        {
            lock (_syncLock)
            {
                if (_processMonitor != null && Process != null)
                {
                    // if we've registered our process with the monitor, unregister
                    _processMonitor.UnregisterChildProcess(Process);
                    _processMonitor = null;
                }
            }
        }

        private void AssignUserExecutePermissionsIfNotExists()
        {
            try
            {
                if (Process is not { } p)
                {
                    return;
                }

                string filePath = p.StartInfo.FileName;
                UnixFileInfo fileInfo = new UnixFileInfo(filePath);
                if (!fileInfo.FileAccessPermissions.HasFlag(FileAccessPermissions.UserExecute))
                {
                    _workerProcessLogger.LogDebug("Assigning execute permissions to file: {filePath}", filePath);
                    fileInfo.FileAccessPermissions |= FileAccessPermissions.UserExecute |
                                                      FileAccessPermissions.GroupExecute |
                                                      FileAccessPermissions.OtherExecute;
                }
            }
            catch (Exception ex)
            {
                _workerProcessLogger.LogWarning(ex, "Error while assigning execute permission.");
            }
        }
    }
}

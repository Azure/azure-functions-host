// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace WorkerHarness.Core.Profiling
{
    internal sealed class ProcessRunner : IDisposable
    {
        private bool _started = false;
        private SemaphoreSlim _semaphoreSlim = new(1, 1);
        private readonly TimeSpan _timeOut;
        private Process? _myProcess;
        private readonly ILogger _logger;
        private TaskCompletionSource<bool>? _processExitedTcs;
        private bool _forceKillProcess;
        public ProcessRunner(TimeSpan timeout, ILogger logger, bool forceKillProcess = false)
        {
            _forceKillProcess = forceKillProcess;
            _timeOut = timeout;
            _logger = logger;
        }
        public TimeSpan ElapsedTime { private set; get; }

        public async Task Run(string fileName, string arguments)
        {
            await _semaphoreSlim.WaitAsync();

            try
            {
                if (_started)
                {
                    throw new InvalidOperationException($"Run method can be called only once in an instance of {nameof(ProcessRunner)}");
                }

                _started = true;
                _processExitedTcs = new TaskCompletionSource<bool>();

                using (_myProcess = new Process())
                {
                    try
                    {
                        _myProcess.StartInfo.FileName = fileName;
                        _myProcess.StartInfo.Arguments = arguments;
                        _myProcess.StartInfo.CreateNoWindow = true;
                        _myProcess.EnableRaisingEvents = true;
                        _myProcess.Exited += new EventHandler(Process_ExitedEventHandler);
                        _logger.LogDebug($"Starting process \"{fileName} {arguments}\"...");
                        _myProcess.Start();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"An error occurred trying to run process \"{fileName} {arguments}\":\n{ex.ToString()}");
                        return;
                    }

                    var doneTask = await Task.WhenAny(_processExitedTcs.Task, Task.Delay(_timeOut));
                    if (doneTask != _processExitedTcs.Task)
                    {
                        if (_forceKillProcess)
                        {
                            _logger.LogWarning($"Process \"{fileName} {arguments}\" timed out after {_timeOut.TotalSeconds} seconds. Killing process...");
                            _myProcess.Kill();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred trying to run process \"{fileName}\":\n{ex.ToString()}");
            }
            finally
            {
                _semaphoreSlim.Release();
            }
        }

        private void Process_ExitedEventHandler(object? sender, EventArgs e)
        {
            if (_myProcess == null)
            {
                return;
            }

            if (sender is Process process)
            {
                _logger.LogDebug($"Process \"{process.StartInfo.FileName}\" exited with code {process.ExitCode}");

                if (process.HasExited == false)
                {
                    var elapsedTs = (_myProcess.ExitTime - _myProcess.StartTime);
                    ElapsedTime = elapsedTs;
                }
            }

            _processExitedTcs?.TrySetResult(true);
        }

        public void Dispose()
        {
            _myProcess?.Dispose();
            _semaphoreSlim?.Dispose();
        }
    }
}
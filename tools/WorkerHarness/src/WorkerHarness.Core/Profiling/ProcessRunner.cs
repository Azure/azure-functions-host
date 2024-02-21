// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace WorkerHarness.Core.Profiling
{
    internal sealed class ProcessRunner : IDisposable
    {
        private bool _started = false;
        private SemaphoreSlim _semaphoreSlim = new(1, 1);
        private readonly TimeSpan _timeOut;
        private Process? _myProcess;
        private TaskCompletionSource<bool>? _processExitedTcs;

        public ProcessRunner(TimeSpan timeout)
        {
            _timeOut = timeout;
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
                        _myProcess.Start();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An error occurred trying to print \"{fileName}\":\n{ex.Message}");
                        return;
                    }

                    await Task.WhenAny(_processExitedTcs.Task, Task.Delay(_timeOut));
                }
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

            var elapsedTs = (_myProcess.ExitTime - _myProcess.StartTime);
            ElapsedTime = elapsedTs;

            _processExitedTcs?.TrySetResult(true);
        }

        public void Dispose()
        {
            _myProcess?.Dispose();
            _semaphoreSlim?.Dispose();
        }
    }
}
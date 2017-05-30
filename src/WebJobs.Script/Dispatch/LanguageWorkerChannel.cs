// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        private readonly LanguageWorkerConfig _config;
        private Process _process;

        public LanguageWorkerChannel(LanguageWorkerConfig config)
        {
            _config = config;
        }

        public Task<object> InvokeAsync(object[] parameters)
        {
            // TODO
            return Task.FromResult<object>(null);
        }

        public Task HandleFileEventAsync(FileSystemEventArgs fileEvent)
        {
            // TODO
            return Task.CompletedTask;
        }

        public Task LoadAsync(FunctionMetadata functionMetadata)
        {
            // TODO
            return Task.CompletedTask;
        }

        public async Task StartAsync()
        {
            await StopAsync();

            string requestId = Guid.NewGuid().ToString();
            Task<IEnumerable<string>> startWorkerTask = StartWorkerAsync(_config, requestId);
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(10));

            // TODO: subscribe to rx with request id, handle timeout, etc
            await Task.WhenAny(startWorkerTask, timeoutTask);
        }

        public Task StopAsync()
        {
            // TODO: send cancellation warning

            _process?.Kill();
            _process = null;
            return Task.CompletedTask;
        }

        internal Task<IEnumerable<string>> StartWorkerAsync(LanguageWorkerConfig config, string requestId)
        {
            var tcs = new TaskCompletionSource<IEnumerable<string>>();

            try
            {
                List<string> output = new List<string>();
                string workerDirectory = Path.GetDirectoryName(config.WorkerPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = config.ExecutablePath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = workerDirectory,
                    Arguments = config.ToArgumentString(requestId)
                };

                void ProcessDataReceived(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        output.Add(e.Data);
                    }
                }

                _process = new Process { StartInfo = startInfo };
                _process.ErrorDataReceived += ProcessDataReceived;
                _process.OutputDataReceived += ProcessDataReceived;
                _process.EnableRaisingEvents = true;
                _process.Exited += (s, e) =>
                {
                    _process.WaitForExit();
                    _process.Close();
                    tcs.SetResult(output);
                };

                _process.Start();

                _process.BeginErrorReadLine();
                _process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                tcs.SetException(exc);
            }

            return tcs.Task;
        }
    }
}

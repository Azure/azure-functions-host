// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace WorkerHarness.Core.WorkerProcess
{
    internal sealed class SystemProcess : IWorkerProcess
    {
        internal Process Process { get; }

        public bool HasExited => Process.HasExited;

        public int Id => Process.Id;

        public SystemProcess(Process process)
        {
            Process = process;

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.Error.WriteLine(e.Data);
                }
            };
        }

        public bool Start()
        {
            var started = Process.Start();
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine();

            return started;
        }

        public void WaitForProcessExit(int milliseconds) => Process.WaitForExit(milliseconds);

        public void Dispose()
        {
            if (Process != null)
            {
                try
                {
                    if (!Process.HasExited)
                    {
                        Process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Handle the exception, e.g. log it or ignore it
                }

                Process.Dispose();
            }
        }
    }
}

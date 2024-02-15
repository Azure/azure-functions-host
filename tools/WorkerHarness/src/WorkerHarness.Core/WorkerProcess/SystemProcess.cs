// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace WorkerHarness.Core.WorkerProcess
{
    internal class SystemProcess : IWorkerProcess
    {
        internal Process Process { get; private set; }

        public bool HasExited => Process.HasExited;

        public SystemProcess(Process process)
        {
            Process = process;
        }

        public bool Start()
        {
            return Process.Start();
        }

        public void WaitForProcessExit(int miliseconds)
        {
            Process.WaitForExit(miliseconds);
        }

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

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace WorkerHarness.Core.WorkerProcess
{
    internal class SystemProcess : IWorkerProcess
    {
        private readonly Process _process;

        internal Process Process => _process;

        public SystemProcess(Process process)
        {
            _process = process;
        }

        public void Kill()
        {
            _process.Kill();
        }

        public bool Start()
        {
            return _process.Start();
        }
    }
}

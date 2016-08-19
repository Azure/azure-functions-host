// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class ProcessManager : IProcessManager
    {
        public IProcessInfo GetCurrentProcess()
        {
            return new ProcessInfo(Process.GetCurrentProcess());
        }

        public IProcessInfo GetProcessById(int processId)
        {
            return new ProcessInfo(Process.GetProcessById(processId));
        }

        public IEnumerable<IProcessInfo> GetProcessesByName(string processName)
        {
            return Process.GetProcessesByName(processName)
                .Select(p => new ProcessInfo(p));
        }
    }
}

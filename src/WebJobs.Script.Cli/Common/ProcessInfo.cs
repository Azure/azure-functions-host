// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Common
{
    internal class ProcessInfo : IProcessInfo
    {
        private readonly Process _process;

        public int Id { get { return _process.Id; } }

        public string FileName { get { return _process.MainModule.FileName; } }

        public string ProcessName { get { return _process.ProcessName; } }

        public ProcessInfo(Process process)
        {
            _process = process;
        }

        public void Kill()
        {
            _process.Kill();
        }
    }
}

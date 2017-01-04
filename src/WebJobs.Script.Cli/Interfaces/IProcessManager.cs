﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace WebJobs.Script.Cli.Interfaces
{
    internal interface IProcessManager
    {
        IEnumerable<IProcessInfo> GetProcessesByName(string processName);
        IProcessInfo GetCurrentProcess();
        IProcessInfo GetProcessById(int processId);
    }
}

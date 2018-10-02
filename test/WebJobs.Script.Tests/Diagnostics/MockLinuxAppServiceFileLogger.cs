// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class MockLinuxAppServiceFileLogger : LinuxAppServiceFileLogger
    {
        public MockLinuxAppServiceFileLogger(string category, string logFileDirectory, IFileSystem fileSystem) : base(category, logFileDirectory, fileSystem)
        {
            Events = new List<string>();
        }

        public List<string> Events { get; }

        public override void Log(string evt)
        {
            Events.Add(evt);
        }

        public override Task ProcessLogQueue(object state)
        {
            return Task.CompletedTask;
        }
    }
}
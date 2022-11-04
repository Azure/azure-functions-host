// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class MockLinuxAppServiceFileLogger : ILinuxAppServiceFileLogger
    {
        public MockLinuxAppServiceFileLogger()
        {
            Events = new List<string>();
        }

        public List<string> Events { get; }

        public void Log(string evt)
        {
            Events.Add(evt);
        }

        public Task ProcessLogQueue(object state)
        {
            return Task.CompletedTask;
        }
    }
}
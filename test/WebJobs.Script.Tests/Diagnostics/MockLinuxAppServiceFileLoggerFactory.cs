// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class MockLinuxAppServiceFileLoggerFactory : ILinuxAppServiceFileLoggerFactory
    {
        public Lazy<ILinuxAppServiceFileLogger> Create(string category, bool backoffEnabled)
        {
            return new Lazy<ILinuxAppServiceFileLogger>(() => new MockLinuxAppServiceFileLogger());
        }
    }
}

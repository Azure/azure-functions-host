// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class MockLinuxAppServiceFileLoggerFactory : ILinuxAppServiceFileLoggerFactory
    {
        public ILinuxAppServiceFileLogger Create(string category, bool logBackoffEnabled)
        {
            return new MockLinuxAppServiceFileLogger();
        }
    }
}
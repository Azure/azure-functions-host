// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public interface ILinuxAppServiceFileLoggerFactory
    {
        public ILinuxAppServiceFileLogger Create(string category, bool backoffEnabled);
    }
}
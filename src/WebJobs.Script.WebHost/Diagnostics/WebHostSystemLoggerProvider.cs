// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// A SystemLoggerProvider that can be registered at the WebHost level. It logs with an empty InstanceId
    /// to make it clear that it is not a part of a JobHost.
    /// </summary>
    public class WebHostSystemLoggerProvider : SystemLoggerProvider
    {
        public WebHostSystemLoggerProvider(IEventGenerator eventGenerator, IEnvironment environment)
            : base(string.Empty, eventGenerator, environment)
        {
        }
    }
}

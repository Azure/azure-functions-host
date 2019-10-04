// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// A SystemLoggerProvider that can be registered at the WebHost level. It logs with an empty InstanceId
    /// to make it clear that it is not a part of a JobHost.
    /// </summary>
    public class WebHostSystemLoggerProvider : SystemLoggerProvider
    {
        private readonly WebJobsEventListener _diagnosticListener;

        public WebHostSystemLoggerProvider(IEventGenerator eventGenerator, IEnvironment environment, IDebugStateProvider debugStateProvider, IScriptEventManager eventManager)
            : base(string.Empty, eventGenerator, environment, debugStateProvider, eventManager)
        {
            _diagnosticListener = new WebJobsEventListener(this);
        }

        public override void Dispose()
        {
            _diagnosticListener?.Dispose();
            base.Dispose();
        }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Dispatch;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerErrorEvent : ScriptEvent
    {
        public WorkerErrorEvent(object worker, Exception exception)
            : base(nameof(WorkerErrorEvent), EventSources.Worker)
        {
            Worker = worker;
            Exception = exception;
        }

        public object Worker { get; private set; }

        public Exception Exception { get; private set; }
    }
}

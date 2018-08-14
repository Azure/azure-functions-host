﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerErrorEvent : ScriptEvent
    {
        internal WorkerErrorEvent(string workerId, Exception exception)
            : base(nameof(WorkerErrorEvent), EventSources.Worker)
        {
            WorkerId = workerId ?? throw new ArgumentNullException(nameof(workerId));
            Exception = exception;
        }

        internal string WorkerId { get; private set; }

        public Exception Exception { get; private set; }
    }
}

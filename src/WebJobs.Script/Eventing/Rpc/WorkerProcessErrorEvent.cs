// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerProcessErrorEvent : ScriptEvent
    {
        internal WorkerProcessErrorEvent(string workerId, string language, Exception exception)
            : base(nameof(WorkerProcessErrorEvent), EventSources.Worker)
        {
            WorkerId = workerId;
            Language = language;
            Exception = exception;
        }

        public string Language { get; }

        public Exception Exception { get; }

        public string WorkerId { get; }
    }
}

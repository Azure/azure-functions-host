// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class HttpWorkerErrorEvent : ScriptEvent
    {
        public HttpWorkerErrorEvent(string workerId, Exception exception, DateTime createdAt = default)
            : base(nameof(HttpWorkerErrorEvent), EventSources.Rpc)
        {
            WorkerId = workerId;
            Exception = exception;
            if (createdAt == default)
            {
                CreatedAt = DateTime.UtcNow;
            }
            else
            {
                CreatedAt = createdAt;
            }
        }

        public string WorkerId { get; }

        public Exception Exception { get; }

        public DateTime CreatedAt { get; }
    }
}

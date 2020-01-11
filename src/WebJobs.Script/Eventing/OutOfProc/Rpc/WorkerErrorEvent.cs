// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerErrorEvent : RpcChannelEvent
    {
        internal WorkerErrorEvent(string language, string workerId, Exception exception, DateTime createdAt = default)
            : base(workerId)
        {
            Exception = exception;
            Language = language;
            if (createdAt == default)
            {
                CreatedAt = DateTime.UtcNow;
            }
            else
            {
                CreatedAt = createdAt;
            }
        }

        internal string Language { get; private set; }

        public Exception Exception { get; private set; }

        public DateTime CreatedAt { get; private set; }
    }
}
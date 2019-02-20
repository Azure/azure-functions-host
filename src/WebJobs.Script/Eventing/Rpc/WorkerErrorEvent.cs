// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerErrorEvent : RpcChannelEvent
    {
        internal WorkerErrorEvent(string language, string workerId, Exception exception)
            : base(workerId)
        {
            Exception = exception;
            Language = language;
        }

        internal string Language { get; private set; }

        public Exception Exception { get; private set; }
    }
}

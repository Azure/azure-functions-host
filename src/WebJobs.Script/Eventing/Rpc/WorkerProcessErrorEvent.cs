// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    public class WorkerProcessErrorEvent : WorkerErrorEvent
    {
        internal WorkerProcessErrorEvent(string workerId, string language, Exception exception)
            : base(workerId, language, exception)
        {
        }
    }
}

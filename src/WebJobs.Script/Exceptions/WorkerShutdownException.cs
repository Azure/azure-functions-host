// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Exceptions
{
    internal class WorkerShutdownException : Exception
    {
        public WorkerShutdownException() { }

        public WorkerShutdownException(string message) : base(message) { }

        public WorkerShutdownException(string message, string reason) : base(message)
        {
            Reason = reason;
        }

        public string Reason { get; set; }
    }
}

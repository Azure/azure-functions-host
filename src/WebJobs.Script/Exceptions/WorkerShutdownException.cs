// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Exceptions
{
    internal sealed class WorkerShutdownException : Exception
    {
        public WorkerShutdownException() { }

        public WorkerShutdownException(string message) : base(message) { }

        public WorkerShutdownException(string message, Exception innerException) : base(message, innerException)
        {
            Reason = innerException?.Message ?? string.Empty;
        }

        public string Reason { get; set; }
    }
}

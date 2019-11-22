// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public class WorkerProcessExitException : Exception
    {
        public WorkerProcessExitException(string message) : base(message)
        {
        }

        public WorkerProcessExitException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public int ExitCode { get; set; }
    }
}

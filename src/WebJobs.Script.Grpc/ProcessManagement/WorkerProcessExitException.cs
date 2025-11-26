// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal class WorkerProcessExitException : Exception
    {
        internal WorkerProcessExitException(string message) : base(message)
        {
        }

        internal WorkerProcessExitException(string message, Exception innerException) : base(message, innerException)
        {
        }

        internal int ExitCode { get; set; }

        internal int Pid { get; set; }
    }
}

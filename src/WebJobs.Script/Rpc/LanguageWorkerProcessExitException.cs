// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerProcessExitException : Exception
    {
        internal LanguageWorkerProcessExitException(string message) : base(message)
        {
        }

        internal LanguageWorkerProcessExitException(string message, Exception innerException) : base(message, innerException)
        {
        }

        internal int ExitCode { get; set; }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Exceptions
{
    internal sealed class FunctionAbortedException : FunctionTimeoutException
    {
        public FunctionAbortedException() { }

        public FunctionAbortedException(string message) : base(message) { }

        public FunctionAbortedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}

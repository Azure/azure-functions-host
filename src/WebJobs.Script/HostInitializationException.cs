// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates an issue initializing a ScriptHost.
    /// </summary>
    public class HostInitializationException : Exception
    {
        public HostInitializationException() { }

        public HostInitializationException(string message) : base(message) { }

        public HostInitializationException(string message, Exception inner) : base(message, inner) { }
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates an issue with the registerd services for a ScriptHost. This will
    /// prevent the host from starting.
    /// </summary>
    [Serializable]
    public class InvalidHostServicesException : Exception
    {
        public InvalidHostServicesException() { }

        public InvalidHostServicesException(string message) : base(message) { }

        public InvalidHostServicesException(string message, Exception inner) : base(message, inner) { }

        protected InvalidHostServicesException(SerializationInfo info, StreamingContext context)
            : base(info, context) { }
    }
}

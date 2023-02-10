// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates an issue calling into an external Startup class. This will
    /// prevent the host from starting.
    /// </summary>
    [Serializable]
    public class ExternalStartupException : Exception
    {
        public ExternalStartupException() { }

        public ExternalStartupException(string message) : base(message) { }

        public ExternalStartupException(string message, Exception inner) : base(message, inner) { }

        protected ExternalStartupException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}

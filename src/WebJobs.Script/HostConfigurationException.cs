// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates an issue configuring a ScriptHost. This will
    /// prevent the host from starting.
    /// </summary>
    [Serializable]
    public class HostConfigurationException : Exception
    {
        public HostConfigurationException() { }

        public HostConfigurationException(string message) : base(message) { }

        public HostConfigurationException(string message, Exception inner) : base(message, inner) { }

        protected HostConfigurationException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}

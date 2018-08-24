// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// An exception that indicates an issue with a function. These exceptions will be caught and
    /// logged, but not cause a host to restart.
    /// </summary>
    [Serializable]
    public class FunctionConfigurationException : Exception
    {
        public FunctionConfigurationException() { }

        public FunctionConfigurationException(string message) : base(message) { }

        public FunctionConfigurationException(string message, Exception inner) : base(message, inner) { }

        protected FunctionConfigurationException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}

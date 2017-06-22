// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script
{
    [Serializable]
    public class ScriptConfigurationException : Exception
    {
        public ScriptConfigurationException() { }

        public ScriptConfigurationException(string message) : base(message) { }

        public ScriptConfigurationException(string message, Exception inner) : base(message, inner) { }

        protected ScriptConfigurationException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}

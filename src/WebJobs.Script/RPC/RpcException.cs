// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    [Serializable]
    public class RpcException : Exception
    {
        public RpcException()
        {
        }

        public RpcException(string message) : base(message)
        {
        }

        public RpcException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public RpcException(string result, string message, string stack)
            : this($"Result: {result}\nException: {message}\nStack: {stack}")
        {
        }

        protected RpcException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}

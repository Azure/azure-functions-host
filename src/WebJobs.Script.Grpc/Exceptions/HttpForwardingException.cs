// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Grpc.Exceptions
{
    internal class HttpForwardingException : Exception
    {
        public HttpForwardingException()
        {
        }

        public HttpForwardingException(string message) : base(message)
        {
        }

        public HttpForwardingException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
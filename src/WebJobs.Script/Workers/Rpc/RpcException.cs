// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Logging;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    public class RpcException : Exception
    {
        public RpcException(string result, string message, string stack, string typeName = "", bool isUserException = false)
            : base($"Result: {result}\nException: {Sanitizer.Sanitize(message)}\nStack: {stack}")
        {
            RemoteStackTrace = stack;
            RemoteMessage = Sanitizer.Sanitize(message);
            if (!string.IsNullOrEmpty(typeName))
            {
                RemoteTypeName = typeName;
            }
            IsUserException = isUserException;
        }

        public bool IsUserException { get; set; }

        public string RemoteStackTrace { get; set; }

        public string RemoteMessage { get; set; }

        public string RemoteTypeName { get; set; }
    }
}

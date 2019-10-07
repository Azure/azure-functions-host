// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class RpcException : Exception
    {
        public RpcException(string result, string message, string stack)
            : base($"Result: {result}\nException: {message}\nStack: {stack}")
        {
        }
    }
}

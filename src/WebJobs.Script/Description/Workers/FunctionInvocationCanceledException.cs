// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public class FunctionInvocationCanceledException : FunctionInvocationException
    {
        public FunctionInvocationCanceledException(string invocationId, Exception innerException)
            : base($"The invocation request with id '{invocationId}' has been cancelled.", innerException) { }
    }
}
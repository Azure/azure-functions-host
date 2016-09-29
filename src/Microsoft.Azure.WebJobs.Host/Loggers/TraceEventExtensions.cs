// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class TraceEventExtensions
    {
        private const string FunctionDescriptorKey = "MS_FunctionDescriptor";
        private const string FunctionInvocationIdKey = "MS_FunctionInvocationId";
        private const string HostInstanceIdKey = "MS_HostInstanceId";

        public static void AddFunctionInstanceDetails(this TraceEvent traceEvent, Guid hostInstanceId, FunctionDescriptor descriptor, Guid functionId)
        {
            traceEvent.Properties[HostInstanceIdKey] = hostInstanceId;
            traceEvent.Properties[FunctionDescriptorKey] = descriptor;
            traceEvent.Properties[FunctionInvocationIdKey] = functionId;
        }
    }
}

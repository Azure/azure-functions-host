// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Abstractions.Description;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    public class FunctionStartedEvent : MetricEvent
    {
        public FunctionStartedEvent(Guid invocationId, FunctionMetadata functionMetadata)
        {
            InvocationId = invocationId;
            FunctionMetadata = functionMetadata;
            Success = true;
            FunctionName = functionMetadata?.Name;
        }

        public FunctionMetadata FunctionMetadata { get; private set; }

        public Guid InvocationId { get; private set; }

        public bool Success { get; set; }
    }
}

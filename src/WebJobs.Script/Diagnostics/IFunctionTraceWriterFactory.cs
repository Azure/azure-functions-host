// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IFunctionTraceWriterFactory
    {
        TraceWriter Create(string functionName, ScriptType? scriptType = null);
    }
}

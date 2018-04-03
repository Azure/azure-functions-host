// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal interface IHostTraceWriterFactory
    {
        TraceWriter Create(TraceLevel level);
    }
}

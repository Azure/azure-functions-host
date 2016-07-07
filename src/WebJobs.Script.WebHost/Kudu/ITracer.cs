// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public interface ITracer
    {
        TraceLevel TraceLevel { get; }
        IDisposable Step(string message, IDictionary<string, string> attributes);
        void Trace(string message, IDictionary<string, string> attributes);
    }
}
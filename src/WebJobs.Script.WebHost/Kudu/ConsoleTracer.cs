﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public class ConsoleTracer : ITracer
    {
        public static readonly ITracer Instance = new ConsoleTracer();

        private ConsoleTracer()
        {
        }

        public TraceLevel TraceLevel
        {
            get { return TraceLevel.Verbose; }
        }

        public IDisposable Step(string value, IDictionary<string, string> attributes)
        {
            return DisposableAction.Noop;
        }

        public void Trace(string value, IDictionary<string, string> attributes)
        {
            // no-op
        }
    }
}
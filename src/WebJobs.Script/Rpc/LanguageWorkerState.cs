// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerState
    {
        private object _lock = new object();

        internal ILanguageWorkerChannel Channel { get; set; }

        internal List<Exception> Errors { get; set; } = new List<Exception>();

        // Registered list of functions which can be replayed if the worker fails to start / errors
        internal ReplaySubject<FunctionMetadata> Functions { get; set; } = new ReplaySubject<FunctionMetadata>();
    }
}

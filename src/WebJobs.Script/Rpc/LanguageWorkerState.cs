// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class LanguageWorkerState
    {
        internal ILanguageWorkerChannel Channel { get; set; }

        internal List<Exception> Errors { get; set; } = new List<Exception>();

        internal List<FunctionRegistrationContext> RegisteredFunctions { get; set; } = new List<FunctionRegistrationContext>();

        // Registered list of functions which can be replayed if the worker fails to start / errors
        internal ReplaySubject<FunctionRegistrationContext> Functions { get; set; } = new ReplaySubject<FunctionRegistrationContext>();
    }
}

// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public class LanguageWorkerState
    {
        private object _lock = new object();
        private IList<FunctionRegistrationContext> _registrations = new List<FunctionRegistrationContext>();

        internal ILanguageWorkerChannel Channel { get; set; }

        internal List<Exception> Errors { get; set; } = new List<Exception>();

        // Registered list of functions which can be replayed if the worker fails to start / errors
        internal ReplaySubject<FunctionRegistrationContext> Functions { get; set; } = new ReplaySubject<FunctionRegistrationContext>();

        internal void AddRegistration(FunctionRegistrationContext registration)
        {
            lock (_lock)
            {
                _registrations.Add(registration);
            }
        }

        internal IEnumerable<FunctionRegistrationContext> GetRegistrations()
        {
            lock (_lock)
            {
                return _registrations.ToList();
            }
        }
    }
}

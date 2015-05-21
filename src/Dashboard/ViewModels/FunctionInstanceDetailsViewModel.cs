// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Dashboard.ViewModels
{
    public class FunctionInstanceDetailsViewModel
    {
        public InvocationLogViewModel Invocation { get; set; }

        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public ParameterModel[] Parameters { get; set; }

        public IEnumerable<Guid> ChildrenIds { get; set; }

        public InvocationLogViewModel Ancestor { get; set; }

        public TriggerReasonViewModel TriggerReason { get; set; }

        public string Trigger { get; set; }

        public bool IsAborted { get; set; }
    }
}

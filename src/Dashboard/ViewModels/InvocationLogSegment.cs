// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Dashboard.ViewModels
{
    /// <summary>Represents a page of function invocation models to send to the browser.</summary>
    public class InvocationLogSegment
    {
        public IEnumerable<InvocationLogViewModel> Entries { get; set; }

        public string ContinuationToken { get; set; }

        public bool IsOldHost { get; set; }
    }
}

// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

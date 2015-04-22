// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IResultSegment<TResult>
    {
        IEnumerable<TResult> Results { get; }

        string ContinuationToken { get; }
    }
}

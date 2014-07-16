// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Dashboard.Data
{
    public interface IResultSegment<TResult>
    {
        IEnumerable<TResult> Results { get; }

        string ContinuationToken { get; }
    }
}

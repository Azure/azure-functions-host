// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Dashboard.Data
{
    public interface IFunctionIndexReader
    {
        DateTimeOffset GetCurrentVersion();

        IResultSegment<FunctionIndexEntry> Read(int maximumResults, string continuationToken);
    }
}

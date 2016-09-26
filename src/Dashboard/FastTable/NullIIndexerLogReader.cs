// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Dashboard.Data.Logs;

namespace Dashboard.Data
{
    internal class NullIIndexerLogReader : IIndexerLogReader
    {
        public IndexerLogEntry ReadWithDetails(string logEntryId)
        {
            throw new NotImplementedException();
        }

        public IResultSegment<IndexerLogEntry> ReadWithoutDetails(int maximumResults, string continuationToken)
        {
            return new ResultSegment<IndexerLogEntry>(new IndexerLogEntry[0], null);
        }
    }
}
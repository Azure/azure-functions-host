// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Dashboard.Data.Logs;

namespace Dashboard.ViewModels
{
    public class IndexerLogEntriesViewModel
    {
        public IEnumerable<IndexerLogEntry> Entries { get; set; }

        public string ContinuationToken { get; set; }
    }
}

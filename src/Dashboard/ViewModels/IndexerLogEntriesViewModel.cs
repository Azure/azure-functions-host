// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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

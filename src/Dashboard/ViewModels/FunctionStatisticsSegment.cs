// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Dashboard.ViewModels
{
    public class FunctionStatisticsSegment
    {
        public IEnumerable<FunctionStatisticsViewModel> Entries { get; set; }
        public string ContinuationToken { get; set; }
        public DateTime? VersionUtc { get; set; }

        // TODO: consider moving these elsewhere as they are not related to functions only
        public string StorageAccountName { get; set; }
        public bool IsOldHost { get; set; }
    }
}

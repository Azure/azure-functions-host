// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace Dashboard.ViewModels
{
    public class PagingInfo
    {
        [Range(1, 100)]
        public int Limit { get; set; }
        public string ContinuationToken { get; set; }
    }
}

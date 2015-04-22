// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

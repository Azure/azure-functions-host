// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Logging
{   
    interface IAggregate
    {
        string PartitionKey
        {
            get;
        }
        string RowKey
        {
            get;        
        }

        int TotalRun { get; set; }

        int TotalPass { get; set; }
        int TotalFail { get; set; }
    } 
}
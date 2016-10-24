// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Describes available function names. 
    // This is useful to quickly query available functions so we can use the names in other point queries. 
    // 1 entity per function definition. 
    internal class FunctionDefinitionEntity : TableEntity, IFunctionDefinition, IEntityWithEpoch
    {
        const string PartitionKeyPrefix = TableScheme.FuncDefIndexPK;
        const string RowKeyFormat = "{0}"; // FunctionId

        DateTime IFunctionDefinition.LastModified
        {
            get
            {
                return this.Timestamp.DateTime;
            }           
        }

        string IFunctionDefinition.Name
        {
            get
            {                
                return OriginalName ?? this.RowKey;
            }
        }

        // Host-aware name. Used in other APIs. 
        // This value is already escaped. 
        FunctionId IFunctionDefinition.FunctionId
        {
            get
            {
                return FunctionId.Parse(this.RowKey);
            }
        }
        

        public DateTime GetEpoch()
        {
            return TimeBucket.CommonEpoch; // Definitions span all epocs 
        }

        // Store the orginal name since functions are case-insensitive, but rowkey must be normalized (table is case-sensitive) 
        // and functions must be case-preserving. 
        public string OriginalName { get; set; }

        public static FunctionDefinitionEntity New(FunctionId functionId, string functionName)
        {
            return new FunctionDefinitionEntity
            {
                PartitionKey = PartitionKeyPrefix,
                RowKey = functionId.ToString(),
                OriginalName = functionName                
            };
        }

        public override string ToString()
        {
            return this.OriginalName;
        }
    }
}
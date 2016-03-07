// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Describes available function names. 
    // This is useful to quickly query available functions so we can use the names in other point queries. 
    // 1 entity per function definition. 
    public class FunctionDefinitionEntity : TableEntity
    {
        const string PartitionKeyFormat = TableScheme.FuncDefIndexPK;
        const string RowKeyFormat = "{0}"; // functionName

        public static FunctionDefinitionEntity New(string functionName)
        {
            return new FunctionDefinitionEntity
            {
                PartitionKey = PartitionKeyFormat,
                RowKey = string.Format(RowKeyFormat, TableScheme.NormalizeFunctionName(functionName))
            };
        }

        public string GetFunctionName()
        {
            return this.RowKey;
        }
    }
}
using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal static class BindableTableEntityPath
    {
        public static IBindableTableEntityPath Create(string tableNamePattern, string partitionKeyPattern,
            string rowKeyPattern)
        {
            List<string> parameterNames = new List<string>();

            BindingDataPath.AddParameterNames(tableNamePattern, parameterNames);
            BindingDataPath.AddParameterNames(partitionKeyPattern, parameterNames);
            BindingDataPath.AddParameterNames(rowKeyPattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedTableEntityPath(tableNamePattern, partitionKeyPattern, rowKeyPattern,
                    parameterNames);
            }

            TableClient.ValidateAzureTableName(tableNamePattern);
            TableClient.ValidateAzureTableKeyValue(partitionKeyPattern);
            TableClient.ValidateAzureTableKeyValue(rowKeyPattern);
            TableEntityPath innerPath = new TableEntityPath(tableNamePattern, partitionKeyPattern, rowKeyPattern);
            return new BoundTableEntityPath(innerPath);
        }
    }
}

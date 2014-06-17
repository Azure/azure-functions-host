using System.Collections.Generic;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal static class BindableTablePath
    {
        public static IBindableTablePath Create(string tableNamePattern)
        {
            List<string> parameterNames = new List<string>();

            BindingDataPath.AddParameterNames(tableNamePattern, parameterNames);

            if (parameterNames.Count > 0)
            {
                return new ParameterizedTablePath(tableNamePattern, parameterNames);
            }

            TableClient.ValidateAzureTableName(tableNamePattern);
            return new BoundTablePath(tableNamePattern);
        }
    }
}

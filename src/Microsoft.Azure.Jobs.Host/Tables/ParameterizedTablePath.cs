using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class ParameterizedTablePath : IBindableTablePath
    {
        private readonly string _tableNamePattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedTablePath(string tableNamePattern, IReadOnlyList<string> parameterNames)
        {
            Debug.Assert(parameterNames.Count > 0);

            _tableNamePattern = tableNamePattern;
            _parameterNames = parameterNames;
        }

        public string TableNamePattern
        {
            get { return _tableNamePattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            IReadOnlyDictionary<string, string> parameters = BindingDataPath.GetParameters(bindingData);
            string tableName = BindingDataPath.Resolve(_tableNamePattern, parameters);

            TableClient.ValidateAzureTableName(tableName);

            return tableName;
        }
    }
}

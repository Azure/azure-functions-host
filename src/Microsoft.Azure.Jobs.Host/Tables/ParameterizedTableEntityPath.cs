// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Azure.Jobs.Host.Bindings;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class ParameterizedTableEntityPath : IBindableTableEntityPath
    {
        private readonly string _tableNamePattern;
        private readonly string _partitionKeyPattern;
        private readonly string _rowKeyPattern;
        private readonly IReadOnlyList<string> _parameterNames;

        public ParameterizedTableEntityPath(string tableNamePattern, string partitionKeyPattern, string rowKeyPattern,
            IReadOnlyList<string> parameterNames)
        {
            Debug.Assert(parameterNames.Count > 0);

            _tableNamePattern = tableNamePattern;
            _partitionKeyPattern = partitionKeyPattern;
            _rowKeyPattern = rowKeyPattern;
            _parameterNames = parameterNames;
        }

        public string TableNamePattern
        {
            get { return _tableNamePattern; }
        }

        public string PartitionKeyPattern
        {
            get { return _partitionKeyPattern; }
        }

        public string RowKeyPattern
        {
            get { return _rowKeyPattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _parameterNames; }
        }

        public TableEntityPath Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            IReadOnlyDictionary<string, string> parameters = BindingDataPath.GetParameters(bindingData);
            string tableName = BindingDataPath.Resolve(_tableNamePattern, parameters);
            string partitionKey = BindingDataPath.Resolve(_partitionKeyPattern, parameters);
            string rowKey = BindingDataPath.Resolve(_rowKeyPattern, parameters);

            TableClient.ValidateAzureTableName(tableName);
            TableClient.ValidateAzureTableKeyValue(partitionKey);
            TableClient.ValidateAzureTableKeyValue(rowKey);

            return new TableEntityPath(tableName, partitionKey, rowKey);
        }

        public override string ToString()
        {
            return _tableNamePattern + "/" + _partitionKeyPattern + "/" + _rowKeyPattern;
        }
    }
}

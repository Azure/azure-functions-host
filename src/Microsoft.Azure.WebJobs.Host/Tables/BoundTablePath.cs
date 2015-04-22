// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class BoundTablePath : IBindableTablePath
    {
        private readonly string _tableName;

        public BoundTablePath(string tableName)
        {
            _tableName = tableName;
        }

        public string TableNamePattern
        {
            get { return _tableName; }
        }

        public bool IsBound
        {
            get { return true; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return Enumerable.Empty<string>(); }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            return _tableName;
        }

        public static string Validate(string value)
        {
            TableClient.ValidateAzureTableName(value);
            return value;
        }
    }
}

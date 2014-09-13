// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class ParameterizedTablePath : IBindableTablePath
    {
        private readonly BindingTemplate _template;

        public ParameterizedTablePath(BindingTemplate template)
        {
            Debug.Assert(template.ParameterNames.Count() > 0);

            _template = template;
        }

        public string TableNamePattern
        {
            get { return _template.Pattern; }
        }

        public bool IsBound
        {
            get { return false; }
        }

        public IEnumerable<string> ParameterNames
        {
            get { return _template.ParameterNames; }
        }

        public string Bind(IReadOnlyDictionary<string, object> bindingData)
        {
            IReadOnlyDictionary<string, string> parameters = BindingDataPath.ConvertParameters(bindingData);
            string tableName = _template.Bind(parameters);

            TableClient.ValidateAzureTableName(tableName);

            return tableName;
        }
    }
}

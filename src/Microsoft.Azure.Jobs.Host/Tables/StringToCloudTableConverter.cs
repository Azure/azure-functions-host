// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class StringToCloudTableConverter : IConverter<string, CloudTable>
    {
        private readonly CloudTableClient _client;
        private readonly IBindableTablePath _defaultPath;

        public StringToCloudTableConverter(CloudTableClient client, IBindableTablePath defaultPath)
        {
            _client = client;
            _defaultPath = defaultPath;
        }

        public CloudTable Convert(string input)
        {
            string tableName;

            // For convenience, treat an an empty string as a request for the default value (when valid).
            if (String.IsNullOrEmpty(input) && _defaultPath.IsBound)
            {
                tableName = _defaultPath.Bind(null);
            }
            else
            {
                tableName = BoundTablePath.Validate(input);
            }

            return _client.GetTableReference(tableName);
        }
    }
}

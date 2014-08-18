// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class StringToTableEntityContextConverter : IConverter<string, TableEntityContext>
    {
        private readonly CloudTableClient _client;
        private readonly IBindableTableEntityPath _defaultPath;

        public StringToTableEntityContextConverter(CloudTableClient client, IBindableTableEntityPath defaultPath)
        {
            _client = client;
            _defaultPath = defaultPath;
        }

        public TableEntityContext Convert(string input)
        {
            TableEntityPath path;

            // For convenience, treat an an empty string as a request for the default value (when valid).
            if (String.IsNullOrEmpty(input) && _defaultPath.IsBound)
            {
                path = _defaultPath.Bind(null);
            }
            else
            {
                path = TableEntityPath.ParseAndValidate(input);
            }

            return new TableEntityContext
            {
                Table = _client.GetTableReference(path.TableName),
                PartitionKey = path.PartitionKey,
                RowKey = path.RowKey
            };
        }
    }
}

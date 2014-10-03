// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class StringToStorageTableConverter : IConverter<string, IStorageTable>
    {
        private readonly IStorageTableClient _client;
        private readonly IBindableTablePath _defaultPath;

        public StringToStorageTableConverter(IStorageTableClient client, IBindableTablePath defaultPath)
        {
            _client = client;
            _defaultPath = defaultPath;
        }

        public IStorageTable Convert(string input)
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

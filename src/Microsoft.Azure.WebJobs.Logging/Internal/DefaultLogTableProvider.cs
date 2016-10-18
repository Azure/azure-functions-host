// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Default table provider for logging 
    internal class DefaultLogTableProvider : ILogTableProvider
    {
        private readonly CloudTableClient _tableClient;
        private readonly string _tableNamePrefix;

        public DefaultLogTableProvider(CloudTableClient tableClient, string tableNamePrefix = LogFactory.DefaultLogTableName)
        {
            if (tableClient == null)
            {
                throw new ArgumentNullException("tableClient");
            }
            if (string.IsNullOrWhiteSpace(tableNamePrefix))
            {
                throw new ArgumentNullException("tableNamePrefix");
            }

            _tableNamePrefix = tableNamePrefix;            
            _tableClient = tableClient;
        }

        public CloudTable GetTable(string suffix)
        {
            var table = LegacyTableReader.TryGetLegacy(_tableClient, suffix);
            if (table != null)
            {
                return table;
            }

            var tableName = _tableNamePrefix + suffix;
            table = _tableClient.GetTableReference(tableName);
            return table;
        }

        // List all tables that we may have handed out. 
        public async Task<CloudTable[]> ListTablesAsync()
        {
            List<CloudTable> list = new List<CloudTable>();
            TableContinuationToken continuationToken = null;
            do
            {
                var segment = await _tableClient.ListTablesSegmentedAsync(_tableNamePrefix, continuationToken, CancellationToken.None);
                list.AddRange(segment.Results);
                continuationToken = segment.ContinuationToken;
            }
            while (continuationToken != null);

            var legacyTable = LegacyTableReader.GetLegacyTable(_tableClient);
            if (legacyTable != null)
            {
                list.Add(legacyTable);
            }

            return list.ToArray();
        }
    }   
}
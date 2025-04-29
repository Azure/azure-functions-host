// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Helpers
{
    public class TableStorageHelpers
    {
        internal static string GetRowKey(DateTime now)
        {
            return string.Format("{0:D19}-{1}", DateTime.MaxValue.Ticks - now.Ticks, Guid.NewGuid());
        }

        internal static async Task<bool> CreateIfNotExistsAsync(TableClient table, TableServiceClient tableClient, int tableCreationRetries, int retryDelayMS = 1000)
        {
            int attempt = 0;
            do
            {
                try
                {
                    if (!await TableExistAsync(table, tableClient))
                    {
                        return (await table.CreateIfNotExistsAsync())?.Value is not null;
                    }
                }
                catch (RequestFailedException rfe)
                {
                    // Can get conflicts with multiple instances attempting to create
                    // the same table.
                    // Also, if a table queued up for deletion, we can get a conflict on create,
                    // though these should only happen in tests not production, because we only ever
                    // delete OLD tables and we'll never be attempting to recreate a table we just
                    // deleted outside of tests.
                    if ((rfe.Status != (int)HttpStatusCode.Conflict || rfe.ErrorCode == TableErrorCode.TableBeingDeleted) &&
                        attempt < tableCreationRetries)
                    {
                        // wait a bit and try again
                        await Task.Delay(retryDelayMS);
                        continue;
                    }
                    throw;
                }
            }
            while (attempt++ < tableCreationRetries);

            return false;
        }

        internal static void QueueBackgroundTablePurge(TableClient currentTable, TableServiceClient tableClient, string tableNamePrefix, ILogger logger, int delaySeconds = 30)
        {
            var tIgnore = Task.Run(async () =>
            {
                try
                {
                    // the deletion is queued with a delay to allow for clock skew across
                    // instances, thus giving time for any in flight operations against the
                    // previous table to complete.
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    await DeleteOldTablesAsync(currentTable, tableClient, tableNamePrefix, logger);
                }
                catch (Exception e)
                {
                    // best effort - if purge fails we log and ignore
                    // we'll try again another time
                    logger.LogError(e, "Error occurred when attempting to delete old diagnostic events tables.");
                }
            });
        }

        internal static async Task DeleteOldTablesAsync(TableClient currentTable, TableServiceClient tableClient, string tableNamePrefix, ILogger logger)
        {
            var tablesToDelete = await ListOldTablesAsync(currentTable, tableClient, tableNamePrefix);
            logger.LogDebug($"Deleting {tablesToDelete.Count()} old tables.");
            foreach (var table in tablesToDelete)
            {
                logger.LogDebug($"Deleting table '{table.Name}'");
                await tableClient.DeleteTableAsync(table.Name);
                logger.LogDebug($"{table.Name} deleted.");
            }
        }

        internal static async Task<IEnumerable<TableClient>> ListOldTablesAsync(TableClient currentTable, TableServiceClient tableClient, string tableNamePrefix)
        {
            var tables = await ListTablesAsync(tableClient, tableNamePrefix);
            return tables.Where(p => !string.Equals(currentTable.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        }

        internal static async Task<IEnumerable<TableClient>> ListTablesAsync(TableServiceClient tableClient, string tableNamePrefix)
        {
            ArgumentNullException.ThrowIfNull(tableClient, nameof(tableClient));

            // Azure.Data.Tables doesn't have a direct way to list tables with a prefix so we need to do it manually
            var givenValue = tableNamePrefix + "{";
            AsyncPageable<TableItem> tablesQuery = tableClient.QueryAsync(p => p.Name.CompareTo(tableNamePrefix) >= 0 && p.Name.CompareTo(givenValue) <= 0);
            var tables = new List<TableClient>();

            await foreach (var table in tablesQuery)
            {
                tables.Add(tableClient.GetTableClient(table.Name));
            }

            return tables;
        }

        internal static async Task<bool> TableExistAsync(TableClient table, TableServiceClient tableClient)
        {
            var query = tableClient.QueryAsync(p => p.Name == table.Name);

            await foreach (var item in query)
            {
                return true;
            }

            return false;
        }

        internal static bool TableExists(TableClient table, TableServiceClient tableClient)
        {
            var query = tableClient.Query(p => p.Name == table.Name);

            foreach (var item in query)
            {
                return true;
            }

            return false;
        }
    }
}

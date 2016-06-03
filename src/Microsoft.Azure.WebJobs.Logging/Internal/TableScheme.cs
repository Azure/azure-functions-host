// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Table;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Central place to organize Table schemes and provide helpers for row manipulation. 
    // Helps ensure partition keys coexist. 
    // - must be able to make bulk updates (so use same partition key)
    internal static class TableScheme
    {
        // List all partition keys in once place to ensure they're disjoint. 
        internal const string InstancePK = "I"; // InstanceTableEntity
        internal const string TimelineAggregatePK = "T"; // TimelineAggregateEntity
        internal const string RecentFuncIndexPK = "R"; // RecentPerFuncEntity
        internal const string ContainerActivePK = "C"; // ContainerActiveEntity
        internal const string FuncDefIndexPK = "FD"; // FunctionDefinitionEntity
        internal const string InstanceCountPK = "IA"; // InstanceCountEntity

        // Read entire partition
        internal static TableQuery<TElement> GetRowsInPartition<TElement>(string partitionKey)
        {
            var query = TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey);
            TableQuery<TElement> rangeQuery = new TableQuery<TElement>().Where(query);
            return rangeQuery;
        }

        // Read rows in the following range
        internal static TableQuery<TElement> GetRowsInRange<TElement>(string partitionKey, string rowKeyStart, string rowKeyEnd)
        {
            return GetRowsInRange<TElement>(partitionKey, rowKeyStart, rowKeyEnd, QueryComparisons.LessThan);
        }                          
        
        private static TableQuery<TElement> GetRowsInRange<TElement>(
            string partitionKey, string rowKeyStart, string rowKeyEnd,
            string endOperator)
        {
            var rowQuery = TableQuery.CombineFilters(
                TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.GreaterThanOrEqual, rowKeyStart),
                TableOperators.And,
                TableQuery.GenerateFilterCondition("RowKey", endOperator, rowKeyEnd));


            var query = TableQuery.CombineFilters(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey),
                TableOperators.And,
                rowQuery);

            
            TableQuery<TElement> rangeQuery = new TableQuery<TElement>().Where(query);
            return rangeQuery;            
        }

        public static string Get1stTerm(string rowKey)
        {
            int i = rowKey.IndexOf('-');
            if (i >= 0)
            {
                return rowKey.Substring(0, i);
            }
            throw new InvalidOperationException("Row key is in illegal format: " + rowKey);
        }

        public static string Get2ndTerm(string rowKey)
        {
            int i = rowKey.IndexOf('-');
            if (i >= 0)
            {
                i++;
                int i2 = rowKey.IndexOf('-', i);
                if (i2 >= 0)
                {
                    int len = (i2 - i);
                    string term = rowKey.Substring(i, len);
                    return term;
                }
            }
            throw new InvalidOperationException("Row key is in illegal format: " + rowKey);
        }

        public static string NormalizeFunctionName(string functionName)
        {
            return functionName.ToLower(CultureInfo.InvariantCulture);
        }

        public static string NormalizeContainerName(string containerName)
        {
            // Remove illegal rowKey chras?
            return containerName.ToLower(CultureInfo.InvariantCulture);
        }
    }
}
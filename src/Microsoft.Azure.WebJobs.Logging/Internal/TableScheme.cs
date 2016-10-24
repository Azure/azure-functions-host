// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Text;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Logging
{
    // Central place to organize Table schemes and provide helpers for row manipulation. 
    // Helps ensure partition keys coexist. 
    // - must be able to make bulk updates (so use same partition key)
    internal static class TableScheme
    {
        // List all partition keys in once place to ensure they're disjoint. 
        internal const string InstancePK = "I"; // InstanceTableEntity
        internal const string TimelineAggregatePK = "T2"; // TimelineAggregateEntity
        internal const string RecentFuncIndexPK = "R2"; // RecentPerFuncEntity
        internal const string ContainerActivePK = "C"; // ContainerActiveEntity
        internal const string FuncDefIndexPK = "FD2"; // FunctionDefinitionEntity
        internal const string InstanceCountPK = "IA"; // InstanceCountEntity

        internal static string GetPartitionKey(string prefix, string hostName)
        {
            return prefix + "-" + NormalizeFunctionName(hostName);
        }

        // Given a rowkey prefix, generate the next prefix. This can be used to find all row keys with a given prefix. 
        internal static string NextRowKey(string rowKeyStart)
        {
            int len = rowKeyStart.Length;
            char ch = rowKeyStart[len - 1];
            char ch2 = (char)(((int)ch) + 1);

            var x = rowKeyStart.Substring(0, len - 1) + ch2;
            return x;
        }

        public static TableQuery<TElement> GetRowsWithPrefixAsync<TElement>(
            string partitionKey,
            string rowKeyPrefix) 
        {
            string rowKeyEnd = NextRowKey(rowKeyPrefix);
            return GetRowsInRange<TElement>(partitionKey, rowKeyPrefix, rowKeyEnd);
        }

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
            return GetNthTerm(rowKey, 1);
        }

        public static string Get2ndTerm(string rowKey)
        {
            return GetNthTerm(rowKey, 2);
        }

        // RowKey - string of parts joined by '-'. 
        // number - 1-based number.
        public static string GetNthTerm(string rowKey, int number)
        {
            int pos = 0;

            while (true)
            {
                int i = rowKey.IndexOf('-', pos);
                if (i == -1)
                {
                    i = rowKey.Length;
                }
                
                if (number == 1)
                {
                    int len = i - pos;
                    string term = rowKey.Substring(pos, len);
                    return term;
                }
                number--;
                if (number == 0)
                {
                    break;
                }
                pos = i + 1;                     
            }
            throw new InvalidOperationException("Row key is in illegal format: " + rowKey);
        }

        private static string EscapeStorageCharacter(char character)
        {
            var ordinalValue = (ushort)character;
            if (ordinalValue < 0x100)
            {
                return string.Format(CultureInfo.InvariantCulture, ":{0:X2}", ordinalValue);
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "::{0:X4}", ordinalValue);
            }
        }

        // Assumes we have a valid function name. 
        // Function names are case-insensitive, case-preserving. 
        // Table storage is case-sensitive. So need to normalize case to use as table keys. 
        // Normalize must be one-to-one to avoid collisions. 
        // Escape any non-alphanumeric characters so that we 
        //  a) have a valid rowkey name 
        //  b) don't have characeters that conflict with separators in the row key (like '-')
        public static string NormalizeFunctionName(string functionName)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var ch in functionName)
            {
                if (ch >= 'a' && ch <= 'z')
                {
                    sb.Append(ch);
                }
                else if (ch >= 'A' && ch <= 'Z')
                {
                    sb.Append((char)(ch - 'A' + 'a'));
                }
                else if (ch >= '0' && ch <= '9')
                {
                    sb.Append(ch);
                }
                else {
                    sb.Append(EscapeStorageCharacter(ch));
                }
            }
            return sb.ToString();            
        }

        public static string NormalizeContainerName(string containerName)
        {
            // Remove illegal rowKey chras?
            return containerName.ToLower(CultureInfo.InvariantCulture);
        }
    }
}
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.Azure.WebJobs.Logging
{
    internal static class Utility
    {
        // See examples here: http://stackoverflow.com/questions/19972443/azure-table-storage-xml-serialization-for-tablecontinuationtoken 
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public static string SerializeToken(TableContinuationToken token)
        {
            if (token == null)
            {
                return null;
            }

            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (var xmlWriter = XmlWriter.Create(writer))
                {
                    token.WriteXml(xmlWriter);
                }
                string serialized = writer.ToString();
                var val = EncodeBase64(serialized);
                return val;
            }

           
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times")]
        public static TableContinuationToken DeserializeToken(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                var raw = DecodeBase64(token);
                TableContinuationToken contToken = null;

                using (var stringReader = new StringReader(raw))
                {
                    contToken = new TableContinuationToken();
                    using (var xmlReader = XmlReader.Create(stringReader))
                    {
                        contToken.ReadXml(xmlReader);
                    }
                }
                return contToken;
            }
            return null;            
        }

        public static string EncodeBase64(string str)
        {            
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            string base64 = Convert.ToBase64String(bytes);
            return base64;
        }

        public static string DecodeBase64(string base64)
        {
            byte[] bytes = Convert.FromBase64String(base64);
            string str = Encoding.UTF8.GetString(bytes);
            return str;
        }


        private static async Task SafeCreateAsync(this CloudTable table, int intervalMilliseconds = 5000, int totalMilliseconds = 120*1000)
        {
            while(true)
            {
                try
                {
                    await table.CreateIfNotExistsAsync();
                    return; 
                }
                catch (StorageException e)
                {
                    var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                    // This can throw 409 if the table is in the process of being deleted.                     
                    if (code != HttpStatusCode.Conflict)
                    {
                        throw;
                    }

                    if (totalMilliseconds < 0)
                    {
                        // timeout. 
                        throw;
                    }
                }
                await Task.Delay(intervalMilliseconds);
                totalMilliseconds -= intervalMilliseconds;
            }
        }


        // Do a query. 
        // If table doesn't exist, return 0-length list of results. 
        public static Task<TElement[]> SafeExecuteQueryAsync<TElement>(this CloudTable table, TableQuery<TElement> query)
            where TElement : ITableEntity, new()
        {
            try
            {
                IEnumerable<TElement> results = table.ExecuteQuery(query);
                var rows = results.ToArray();
                return Task.FromResult(rows);
            }
            catch (StorageException e)
            {
                var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                if (code == HttpStatusCode.NotFound)
                {
                    return Task.FromResult(new TElement[0]);
                }
                throw;
            }
        }

        // Do a query
        // If table doesn't exist, return null. 
        public static async Task<TableQuerySegment<TElement>> SafeExecuteQuerySegmentedAsync<TElement>(
            this CloudTable table,
            TableQuery<TElement> query, 
            TableContinuationToken token, 
            CancellationToken cancellationToken)
        where TElement : ITableEntity, new()
        {
            try
            {
                var segment = await table.ExecuteQuerySegmentedAsync<TElement>(
                  query,
                  token,
                  cancellationToken);
                return segment;
            }
            catch (StorageException e)
            {
                var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                if (code == HttpStatusCode.NotFound)
                {
                    // TableQuerySegment ctor is private, so return null. 
                    return null;
                }
                throw;
            }
        }

        // Write table entry. 
        // If table doesn't exist (such as if it was deleted), then recreate it. 
        public static async Task SafeExecuteAsync(this CloudTable table, TableBatchOperation batch)
        {
            try
            {
                await table.ExecuteBatchAsync(batch);
                return;
            }
            catch (StorageException e)
            {
                var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                if (code != HttpStatusCode.NotFound)
                {
                    throw;
                }
            }

            await table.SafeCreateAsync();
            await table.ExecuteBatchAsync(batch);
        }

        public static async Task<TableResult> SafeExecuteAsync(this CloudTable table, TableOperation operation)
        {
            try
            {
                return await table.ExecuteAsync(operation);
            }
            catch (StorageException e)
            {
                var code = (HttpStatusCode)e.RequestInformation.HttpStatusCode;
                if (code != HttpStatusCode.NotFound)
                {
                    throw;
                }
            }

            await table.SafeCreateAsync();  
            return await table.ExecuteAsync(operation);
        }
    }
}

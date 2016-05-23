// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ApiHub.Sdk.Common;
using Microsoft.Azure.ApiHub.Sdk.Table;
using Microsoft.Azure.ApiHub.Sdk.Table.Internal;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApiHub
{
    internal class FakeTabularConnectorAdapter : ITabularConnectorAdapter
    {
        private const string DataSetPrefix = "D";
        private const string TablePrefix = "T";
        private const string EntityPrefix = "E";
        private const string KeySeparator = "-";
        private const string PrimaryKey = "PrimaryKey";

        public FakeTabularConnectorAdapter()
        {
            Metadata = new Dictionary<string, TableMetadata>();
            Objects = new Dictionary<string, object>();
        }

        private Dictionary<string, TableMetadata> Metadata { get; }
        private Dictionary<string, object> Objects { get; }

        public virtual Task<SegmentedResult<IDataSet>> ListDataSetsAsync(
            Query query = null, 
            ContinuationToken continuationToken = null, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new SegmentedResult<IDataSet>
            {
                Items = Objects
                    .Where(kv => kv.Key.StartsWith(DataSetsPrefix()))
                    .Select(kv => (IDataSet)kv.Value)
                    .ToList()
            };

            return Task.FromResult(result);
        }

        public virtual Task<SegmentedResult<ITable<JObject>>> ListTablesAsync(
            string dataSetName, 
            Query query = null, 
            ContinuationToken continuationToken = null, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new SegmentedResult<ITable<JObject>>
            {
                Items = Objects
                    .Where(kv => kv.Key.StartsWith(TablesPrefix(dataSetName)))
                    .Select(kv => (ITable<JObject>)kv.Value)
                    .ToList()
            };

            return Task.FromResult(result);
        }

        public virtual Task<TableMetadata> GetTableMetadataAsync(
            string dataSetName, 
            string tableName, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var key = TableKey(dataSetName, tableName);
            TableMetadata metadata;
            if (!Metadata.TryGetValue(key, out metadata))
            {
                Task.FromResult<TableMetadata>(null);
            }

            return Task.FromResult(metadata);
        }

        public virtual Task<TEntity> GetEntityAsync<TEntity>(
            string dataSetName, 
            string tableName, 
            string entityId, 
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : class
        {
            var key = EntityKey(dataSetName, tableName, entityId);
            object entityObj;
            if (!Objects.TryGetValue(key, out entityObj))
            {
                return Task.FromResult<TEntity>(null);
            }

            var entity = ((JObject)entityObj).ToObject<TEntity>();
            return Task.FromResult(entity);
        }

        public virtual Task<SegmentedResult<TEntity>> ListEntitiesAsync<TEntity>(
            string dataSetName, 
            string tableName, 
            Query query = null, 
            ContinuationToken continuationToken = null, 
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : class
        {
            var result = new SegmentedResult<TEntity>
            {
                Items = Objects
                    .Where(kv => kv.Key.StartsWith(EntitiesPrefix(dataSetName, tableName)))
                    .Select(kv => ((JObject)kv.Value).ToObject<TEntity>())
                    .ToList()
            };

            return Task.FromResult(result);
        }

        public virtual Task CreateEntityAsync<TEntity>(
            string dataSetName, 
            string tableName, 
            TEntity entity, 
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : class
        {
            var metadata = GetTableMetadataAsync(dataSetName, tableName).Result;
            var primaryKey = metadata.Schema[PrimaryKey].Value<string>();
            var entityObj = JObject.FromObject(entity);
            var entityId = entityObj[primaryKey].Value<string>();
            var key = EntityKey(dataSetName, tableName, entityId);

            Objects.Add(key, entityObj);

            return Task.FromResult(0);
        }

        public virtual Task UpdateEntityAsync<TEntity>(
            string dataSetName, 
            string tableName, 
            string entityId, 
            TEntity entity, 
            CancellationToken cancellationToken = default(CancellationToken))
            where TEntity : class
        {
            var key = EntityKey(dataSetName, tableName, entityId);

            if (!Objects.ContainsKey(key))
            {
                throw new KeyNotFoundException();
            }

            Objects[key] = JObject.FromObject(entity);

            return Task.FromResult(0);
        }

        public virtual Task DeleteEntityAsync(
            string dataSetName, 
            string tableName, 
            string entityId, 
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var key = EntityKey(dataSetName, tableName, entityId);

            Objects.Remove(key);

            return Task.FromResult(0);
        }

        public void AddDataSet(string dataSetName)
        {
            var key = DataSetKey(dataSetName);
            var dataSet = new DataSet(dataSetName, this);

            Objects.Add(key, dataSet);
        }

        public void AddTable(string dataSetName, string tableName, string primaryKey)
        {
            var key = TableKey(dataSetName, tableName);
            var schema = new JObject(new JProperty(PrimaryKey, primaryKey));
            var metadata = new TableMetadata
            {
                Name = tableName,
                Schema = schema
            };
            var table = new Table<JObject>(dataSetName, tableName, this);

            Metadata.Add(key, metadata);
            Objects.Add(key, table);
        }

        private static string DataSetKey(string dataSetName)
        {
            return string.Concat(DataSetPrefix, KeySeparator, dataSetName);
        }

        private static string TableKey(string dataSetName, string tableName)
        {
            return string.Concat(DataSetKey(dataSetName), KeySeparator, tableName);
        }

        private static string EntityKey(string dataSetName, string tableName, string entityId)
        {
            return string.Concat(TableKey(dataSetName, tableName), KeySeparator, entityId);
        }

        private string DataSetsPrefix()
        {
            return string.Concat(DataSetPrefix, KeySeparator);
        }

        private string TablesPrefix(string dataSetName)
        {
            return string.Concat(DataSetKey(dataSetName), KeySeparator);
        }

        private string EntitiesPrefix(string dataSetName, string tableName)
        {
            return string.Concat(TableKey(dataSetName, tableName), KeySeparator);
        }
    }
}

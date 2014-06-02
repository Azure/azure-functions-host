using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    // Azure storage emulator is doesn't support Upsert, so we'd need to run against live storage.
    // Instead, use a test hook to mock out the storage and use In-Memory azure tables.
    public class TableTests
    {
        // Mock out Azure Table storage. 
        class InMemoryTableProviderTestHook : TableProviderTestHook
        {
            public override ICloudTableClient Create(string accountConnectionString)
            {
                return new TestCloudTableClient();
            }
        }

        [Fact]
        public void TableEntity()
        {
            var store = new InMemoryTableProviderTestHook();
            TableProviderTestHook.Default = store;
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            // Write via traditional Azure APIs.
            ICloudTable table = store.Create(null).GetTableReference(TableProgram.TableEntityName);
            table.InsertOrReplace(new DynamicTableEntity()
                {
                    PartitionKey = TableProgram.TableEntityPartitionKey,
                    RowKey = TableProgram.TableEntityRowKey,
                    Properties = new Dictionary<string, EntityProperty>
                    {
                        { "Fruit", new EntityProperty("Banana") },
                        { "Duration", new EntityProperty("1") },
                        { "Value", new EntityProperty("Foo") }
                    }
                });

            var lc = TestStorage.New<TableProgram>(account);
            lc.Call("TableEntity");

            // Read via traditional Azure APIs.
            var results = table.Query<DynamicTableEntity>(null);

            Assert.Equal(1, results.Count());
            DynamicTableEntity result = results.ElementAt(0);
            var properties = result.Properties;
            Assert.Equal("Pear", properties["Fruit"].StringValue);
            Assert.Equal("2", properties["Duration"].StringValue);
            Assert.Equal("Bar", properties["Value"].StringValue);
        }

        public class TableEntry : TableEntity
        {
            public string myvalue { get; set; }
        }

        class TableProgram
        {
            public const string TableEntityName = "testtable3";
            public const string TableEntityPartitionKey = "PK";
            public const string TableEntityRowKey = "RK";
            public static void TableEntity([Table(TableEntityName, TableEntityPartitionKey, TableEntityRowKey)] OtherStuff entity)
            {
                Assert.Equal(Fruit.Banana, entity.Fruit);
                Assert.Equal(TimeSpan.FromSeconds(1), entity.Duration);
                Assert.Equal("Foo", entity.Value);

                entity.Fruit = Fruit.Pear;
                entity.Duration = TimeSpan.FromMinutes(2);
                entity.Value = "Bar";
            }

        } // program

        // Type with some problematic data types for TableServiceEntity
        public class OtherStuff
        {
            public Fruit Fruit { get; set; }
            public TimeSpan Duration { get; set; }
            public string Value { get; set; }
        }

        public enum Fruit
        {
            Apple,
            Banana,
            Pear,
        }

        public class Stuff
        {
            public string RowKey { get; set; }
            public int myvalue { get; set; }
        }

        private class TestCloudTableClient : ICloudTableClient
        {
            public ICloudTable GetTableReference(string tableName)
            {
                throw new NotImplementedException();
            }

            private class TestCloudTable : ICloudTable
            {
                private IDictionary<Tuple<string, string>, ITableEntity> _store =
                    new Dictionary<Tuple<string, string>, ITableEntity>();

                public TElement Retrieve<TElement>(string partitionKey, string rowKey) where TElement : class, ITableEntity
                {
                    Tuple<string, string> key = new Tuple<string, string>(partitionKey, rowKey);

                    if (!_store.ContainsKey(key))
                    {
                        return null;
                    }

                    return (TElement)_store[key];
                }

                public void Insert(ITableEntity entity)
                {
                    throw new NotImplementedException();
                }

                public void Insert(IEnumerable<ITableEntity> entities)
                {
                    throw new NotImplementedException();
                }

                public void InsertOrReplace(ITableEntity entity)
                {
                    _store[new Tuple<string, string>(entity.PartitionKey, entity.RowKey)] = entity;
                }

                public void InsertOrReplace(IEnumerable<ITableEntity> entities)
                {
                    throw new NotImplementedException();
                }

                public void Replace(ITableEntity entity)
                {
                    throw new NotImplementedException();
                }

                public void Replace(IEnumerable<ITableEntity> entities)
                {
                    throw new NotImplementedException();
                }

                public IEnumerable<TElement> Query<TElement>(int? limit, params IQueryModifier[] queryModifiers) where TElement : ITableEntity, new()
                {
                    if (queryModifiers != null && queryModifiers.Length != 0)
                    {
                        throw new NotImplementedException();
                    }

                    foreach (ITableEntity value in _store.Values)
                    {
                        yield return (TElement)value;
                    }
                }

                public TElement GetOrInsert<TElement>(TElement entity) where TElement : ITableEntity, new()
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}

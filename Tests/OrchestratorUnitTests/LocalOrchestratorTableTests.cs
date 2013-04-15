using System;
using System.Collections.Generic;
using System.Data.Services.Common;
using System.Reflection;
using AzureTables;
using SimpleBatch;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System.Linq;
using RunnerHost;
using Microsoft.WindowsAzure;

namespace LocalOrchestratorTableTests
{
    // Azure storage emulator is doesn't support Upsert, so we'd need to run against live storage.
    // Instead, use a test hook to mock out the storage and use In-Memory azure tables.
    [TestClass]
    public class TableTests
    {
        // Mock out Azure Table storage. 
        class InMemoryTableProviderTestHook : TableProviderTestHook
        {
            Dictionary<string, AzureTable> _tables = new Dictionary<string, AzureTable>();

            public override AzureTable Create(string accountConnectionString, string tableName)
            {
                AzureTable table;
                if (!_tables.TryGetValue(tableName, out table))
                {
                    table = AzureTable.NewInMemory();
                    _tables[tableName] = table;
                }
                return table;
            }
            public override AzureTable<T> Create<T>(string accountConnectionString, string tableName)
            {
                var table = Create(accountConnectionString, tableName);
                return table.GetTypeSafeWrapper<T>();
            }
        }

        // Azure storage emulator is doesn't support Upsert, so table won't work. 
        [TestMethod]
        public void TableDict()
        {
            var store = new InMemoryTableProviderTestHook();
            TableProviderTestHook.Default = store;
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            MethodInfo m = typeof(TableProgram).GetMethod("TableDict");
            LocalOrchestrator.Invoke(account, m);
        }

        // Azure storage emulator is doesn't support Upsert, so table won't work. 
        [TestMethod]
        public void Table()
        {
            var store = new InMemoryTableProviderTestHook();
            TableProviderTestHook.Default = store;
            var account = CloudStorageAccount.DevelopmentStorageAccount;
                        

            MethodInfo m = typeof(TableProgram).GetMethod("TableWrite");
            LocalOrchestrator.Invoke(account, m);

            // Read via traditional Azure APIs.
            IAzureTable<TableEntry> table = store.Create<TableEntry>(null, TableProgram.TableName);
            var results = table.Enumerate().ToArray();
                        
            Assert.AreEqual(10, results.Length);
            Assert.AreEqual("part", results[2].PartitionKey);
            Assert.AreEqual("2", results[2].RowKey);
            Assert.AreEqual("20", results[2].myvalue);

            MethodInfo m2 = typeof(TableProgram).GetMethod("TableRead");
            LocalOrchestrator.Invoke(account, m2);

            MethodInfo m3 = typeof(TableProgram).GetMethod("TableReadStrong");
            LocalOrchestrator.Invoke(account, m3);
        }

        [TestMethod]
        public void TableReadWrite()
        {
            var store = new InMemoryTableProviderTestHook();
            TableProviderTestHook.Default = store;
            var account = CloudStorageAccount.DevelopmentStorageAccount;

            MethodInfo m = typeof(TableProgram).GetMethod("TableReadWrite");
            LocalOrchestrator.Invoke(account, m);
        }

        [DataServiceKey("PartitionKey", "RowKey")]
        public class TableEntry : TableServiceEntity
        {
            public string myvalue { get; set; }
        }

        class TableProgram
        {
            public const string TableName = "testtable1";

            public static void TableWrite([Table(TableName)] IAzureTableWriter writer)
            {
                for (int i = 0; i < 10; i++)
                {
                    writer.Write("part", i.ToString(), new { myvalue = i * 10 });
                }
            }

            public static void TableRead([Table(TableName)] IAzureTableReader reader)
            {
                for (int i = 0; i < 10; i++)
                {
                    var d = reader.Lookup("part", i.ToString());
                    Assert.AreEqual(4, d.Count);
                    Assert.IsTrue(d.ContainsKey("Timestamp")); // beware of casing!
                    Assert.IsTrue(d.ContainsKey("RowKey")); // beware of casing!
                    Assert.IsTrue(d.ContainsKey("PartitionKey")); // beware of casing!

                    var val = d["myvalue"];
                    Assert.AreEqual((i * 10).ToString(), val);
                }
            }

            public static void TableReadStrong([Table(TableName)] IAzureTableReader<Stuff> reader)
            {
                for (int i = 0; i < 10; i++)
                {
                    Stuff d = reader.Lookup("part", i.ToString());

                    Assert.AreEqual(i.ToString(), d.RowKey);
                    Assert.AreEqual(i * 10, d.myvalue);
                }
            }

            public static void TableReadWrite([Table(TableName)] IAzureTable table)
            {
                var val = table.Lookup("x", "y");
                Assert.IsNull(val); // not there yet

                table.Write("x", "y", new { myvalue = 50 });

                val = table.Lookup("x", "y");
                Assert.AreEqual("50", val["myvalue"]);
            }


            public const string TableNameDict = "testtable2";
            public static void TableDict([Table(TableNameDict)] IDictionary<Tuple<string, string>, OtherStuff> dict)
            {
                // Use IDictionary interface to access an azure table.
                var partRowKey = Tuple.Create("x", "y");
                OtherStuff val;
                bool found = dict.TryGetValue(partRowKey, out val);
                Assert.IsFalse(found, "item should not be found");

                dict[partRowKey] = new OtherStuff { Value = "fall", Fruit = Fruit.Apple, Duration = TimeSpan.FromMinutes(5) };

                // Enumerate, should find the write
                int count = 0;
                foreach (var kv in dict)
                {
                    // Only 1 item.
                    Assert.AreEqual(partRowKey, kv.Key);
                    Assert.AreEqual(false, object.ReferenceEquals(partRowKey, kv.Key));

                    Assert.AreEqual(Fruit.Apple, kv.Value.Fruit);
                    Assert.AreEqual(TimeSpan.FromMinutes(5), kv.Value.Duration);
                    Assert.AreEqual("fall", kv.Value.Value);

                    count++;
                }
                Assert.AreEqual(1, count);

                // Clear
                dict.Remove(partRowKey);

                count = 0; // no Count property on an AzureTable, and Count() extension method just calls Count prop. 
                foreach (var kv in dict)
                {
                    count++;
                }
                Assert.AreEqual(0, count);
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
    }
}

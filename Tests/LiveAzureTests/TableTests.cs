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

namespace LiveAzureTests
{
    // Azure storage emulator is doesn't support Upsert, so table won't work. 
    // Need to run table tests against live storage
    [TestClass]
    public class TableTests
    {
        // Azure storage emulator is doesn't support Upsert, so table won't work. 
        [TestMethod]
        public void TableDict()
        {
            var account = AzureConfig.GetAccount(); // Need live account for this test to work.
            
            MethodInfo m = typeof(TableProgram).GetMethod("TableDict");
            LocalOrchestrator.Invoke(account, m);
        }

        [TestMethod]
        public void AzureTableClassEnum()
        {
            AzureTable table = new AzureTable(AzureConfig.GetAccount(), "test2");
            table.Clear();

            table.Write("1", "x", new { Fruit = Fruit.Banana });

            // Read normally. Should be serialized as a textual name, not a number
            var x = table.Lookup("1", "x");
            Assert.IsNotNull(x);
            Assert.AreEqual("Banana", x["Fruit"]); 

            // Read with strong binder.
            IAzureTableReader<FruitEntity> reader = table.GetTypeSafeWrapper<FruitEntity>();
            FruitEntity f = reader.Lookup("1", "x");
            Assert.AreEqual(Fruit.Banana, f.Fruit);
        }

        class FruitEntity
        {
            public Fruit Fruit { get; set; }
        }

        public enum Fruit
        {
            Apple,
            Banana,
            Pear,
        }

        // This test takes 2 mins.
        //[TestMethod]
        public void AzureTableClassDelete()
        {
            var tableName = "testTable" + DateTime.Now.Ticks.ToString();
            AzureTable table = new AzureTable(AzureConfig.GetAccount(), tableName);
            try
            {

                int N = 2000; // Pick > 1000 to deal with azure corner cases 
                for (int i = 0; i < N; i++)
                {
                    table.Write("1", i.ToString(), new { val = i * 10 });
                }
                table.Write("2", "x", new { val = 15 });

                // Delete a single row.
                {
                    var x = table.Lookup("1", "90");
                    Assert.IsNotNull(x);
                    Assert.AreEqual("900", x["val"]);

                    table.Delete("1", "90");
                    var x2 = table.Lookup("1", "90");
                    Assert.IsNull(x2, "item should be deleted");
                }

                // Delete the whole partition
                {
                    var all = table.Enumerate("1");
                    int count = all.Count();
                    Assert.AreEqual(N - 1, count);

                    table.Delete("1");  // This could take minutes. 

                    var all2 = table.Enumerate("1");
                    int count2 = all2.Count();
                    Assert.AreEqual(0, count2);

                    // Verify we didn't delete other partitions
                    var x = table.Lookup("2", "x");
                    Assert.IsNotNull(x);
                }
            }
            finally
            {
                table.ClearAsync();
            }
        }

        // Azure storage emulator is doesn't support Upsert, so table won't work. 
        [TestMethod]
        public void Table()
        {
            var account =  AzureConfig.GetAccount(); // Need live account for this test to work.

            Utility.DeleteTable(account, TableProgram.TableName);

            MethodInfo m = typeof(TableProgram).GetMethod("TableWrite");
            LocalOrchestrator.Invoke(account, m);

            // Read via traditional Azure APIs.
            var results = Utility.ReadTable<TableEntry>(account, TableProgram.TableName);
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
            var account = AzureConfig.GetAccount(); // Need live account for this test to work.

            Utility.DeleteTable(account, TableProgram.TableName);

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
                    Assert.AreEqual(2, d.Count);
                    Assert.IsTrue(d.ContainsKey("Timestamp")); // beware of casing!

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

        public class Stuff
        {
            public string RowKey { get; set; }
            public int myvalue { get; set; }
        }
    }
}
